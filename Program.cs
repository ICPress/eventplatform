// See https://aka.ms/new-console-template for more information
using System.Text;
using System.Text.Json;
using Apache.Ignite.Core;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using MySql.Data.MySqlClient;
using Apache.Ignite.Core.Binary;
using Microsoft.Extensions.Configuration;
using Apache.Ignite.Core.Client;
using Apache.Ignite.Core.Events;

var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"); // DOTNET_ENVIRONMENT
IConfiguration configuration = new ConfigurationBuilder()
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile($"appsettings.{environmentName}.json", true, true)
        .Build();

ServerSettings? settings = configuration.GetRequiredSection("ServerSettings").Get<ServerSettings>();
if (settings == null)
{
    Console.WriteLine("Appsettings is not initialized!");
    return;
}

// Configure the HTTP request pipeline.
Console.WriteLine("Running environment:" + environmentName + ", gorse:" + settings.GorseAPIEndpoint);

if (settings != null && (new DirectoryInfo(settings.FirebaseSDKCredentialsJson)).Exists)
{
    try
    {
        var firbaseApp = FirebaseApp.Create(new AppOptions()
        {
            Credential = GoogleCredential.FromFile(settings.FirebaseSDKCredentialsJson),
        });
        Console.WriteLine("Initialized firebase app:" + firbaseApp.Name);
    }
    catch (Exception ex)
    {
        Console.WriteLine("Could not initialize firebase:" + ex.Message,ex);
    }
}else Console.WriteLine("Skipping Initialization of firebase, SDK credentials file is missing:" + settings?.FirebaseSDKCredentialsJson);

while (true)
{
    using (MySqlConnection connection = new MySqlConnection(settings.MysqlConnectionStoryPop))
    {
        try
        {
            var queueList = new List<EventQueueRow>(50);
            await connection.OpenAsync();
            var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand.CommandText = "SELECT event_id, trigger_source_username , additional_data , type , claimed  from events_queued where available_from <= CURRENT_TIMESTAMP order by event_id limit 50";
            mySqlCommand.Connection = connection;
            using var reader = mySqlCommand.ExecuteReader();
            try
            {
                while (await reader.ReadAsync())
                {
                    var queueRow = new EventQueueRow();
                    queueRow.event_id = reader.GetUInt32("event_id");
                    queueRow.trigger_source_username = reader.GetString("trigger_source_username");
                    queueRow.additional_data = reader.GetString("additional_data");
                    queueRow.type = (EventTriggerType)reader.GetUInt16("type"); ;
                    queueRow.claimed = reader.GetBoolean("claimed");
                    if (queueRow.claimed) continue;
                    else queueList.Add(queueRow);
                }
            }
            finally
            {
                await reader.CloseAsync();
            }
            foreach (EventQueueRow eventRow in queueList)
            {
                var mySqlCommand2 = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommand2.CommandText = "UPDATE events_queued SET claimed=1  WHERE event_id=@event_id and claimed = 0";
                mySqlCommand2.Connection = connection;
                mySqlCommand2.Parameters.AddWithValue("@event_id", eventRow.event_id);
                if ((long?)await mySqlCommand2.ExecuteNonQueryAsync() != 1) continue;
                eventRow.skipEvent = await EventUtil.CheckSkipEvent(connection, eventRow.trigger_source_username, eventRow.additional_data, eventRow.event_id, eventRow.type);
                if (eventRow.skipEvent) continue;
                var targetUsername = eventRow.additional_data;
                switch (eventRow.type)
                {
                    case EventTriggerType.FOLLOW_USER:
                        await EventProcessor.HandleFollowUser(connection, targetUsername, eventRow);
                        break;
                    case EventTriggerType.UNFOLLOW_USER:
                        await EventProcessor.HandleUnfollowUser(connection, eventRow);
                        break;
                    case EventTriggerType.LIKE:
                        await EventProcessor.HandleLike(connection, eventRow, settings);
                        break;
                    case EventTriggerType.PUBLISHED_ARTICLE:
                        await EventProcessor.HandlePublishedArticle(connection, eventRow, settings);
                        //await EventProcessor.GenerateWeeklyAwards(eventRow.trigger_source_username, connection); //generate weekly award
                        break;
                    case EventTriggerType.SPECIAL_REWARD:
                        await EventUtil.CreateReward(connection, eventRow.trigger_source_username, eventRow.additional_data, TransactionDescriptionType.SPECIAL_REWARD, 250, TransactionType.STORY_POINTS_REWARD);
                        break;
                    case EventTriggerType.STORY_POINTS_NOTIFICATION:
                        var devices = FirebaseMessagingUtil.GetUserDevices(eventRow.trigger_source_username, connection);
                        var fcmMessages = new List<FirebaseAdmin.Messaging.Message>();
                        foreach (var deviceToken in devices)
                        {
                            fcmMessages.Add(FirebaseMessagingUtil.CreateHighPriorityNotificationMessage("StoryPoints waiting to be used", "Use your StoryPoints and claim your rewards.", deviceToken));
                        }
                        await FirebaseMessagingUtil.SendFCMMultipleDeviceNotification(fcmMessages);
                        break;
                    case EventTriggerType.FCM_TEST_MESSAGE:
                        var testdevices = FirebaseMessagingUtil.GetUserDevices(eventRow.trigger_source_username, connection);
                        var testMessages = FirebaseMessagingUtil.CreateSilentAddonNotificationMessages(testdevices, 1, 1);
                        await FirebaseMessagingUtil.SendFCMMultipleDeviceNotification(testMessages);
                        break;
                    case EventTriggerType.WEEKLY_REWARD_MANUAL_TRIGGER:
                        await EventProcessor.GenerateWeeklyAwards(eventRow.trigger_source_username, connection);
                        break;
                    case EventTriggerType.TERMINATE_ACCOUNT:
                        await EventUtil.TerminateAccount(settings, connection, eventRow.trigger_source_username);
                        break;
                    case EventTriggerType.WRITE_COMMENT:
                    case EventTriggerType.LIKE_COMMENT:
                    case EventTriggerType.REPLY_COMMENT:
                        await EventProcessor.HandleCommentAction(connection, targetUsername, eventRow, settings);
                        break;
                    case EventTriggerType.LIKE_COMMENT_DELETE:
                        break;
                    case EventTriggerType.REMOVE_ARTICLE:
                        await EventProcessor.HandleRemoveArticle(connection, eventRow, settings);
                        break;
                    case EventTriggerType.WRITE_MESSAGE:
                        targetUsername = eventRow.additional_data.Substring(0, eventRow.additional_data.IndexOf(":"));
                        await EventProcessor.HandleWriteMessage(connection, targetUsername, eventRow, settings);
                        break;
                    case EventTriggerType.NEW_MESSAGE_NOTIFIATION:
                        targetUsername = eventRow.additional_data.Substring(0, eventRow.additional_data.IndexOf(":"));
                        await EventProcessor.HandleNewMessageNotification(connection, targetUsername, eventRow);
                        break;
                }
                var mySqlCommandFinish = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandFinish.CommandText = "DELETE FROM events_queued WHERE event_id=@event_id";
                mySqlCommandFinish.Connection = connection;
                mySqlCommandFinish.Parameters.AddWithValue("@event_id", eventRow.event_id);
                await mySqlCommandFinish.ExecuteNonQueryAsync();
            }
            queueList.Clear();

        }
        catch (Exception ex)
        {
            Console.WriteLine("Error while processing events:"+ex.Message,ex);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }
    //Console.WriteLine("Processed all fetched events for now, going for short sleep!");
    await Task.Delay(15000); //15 sec
}







