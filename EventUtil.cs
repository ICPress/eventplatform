using Apache.Ignite.Core;
using Apache.Ignite.Core.Binary;
using Apache.Ignite.Core.Client;
using MySql.Data.MySqlClient;

public static class EventUtil
{
public static async Task<bool> CheckSkipEvent(MySqlConnection connection, string trigger_source_username, string additional_data, uint skip_event_id, EventTriggerType checkEventType, int limit = 0)
{
    var mySqlCommand3 = new MySql.Data.MySqlClient.MySqlCommand();
    mySqlCommand3.CommandText = "SELECT COUNT(*) FROM events_queued WHERE trigger_source_username=@username and additional_data=@additional_data and event_id <> @event_id and type=@type";
    mySqlCommand3.Connection = connection;
    mySqlCommand3.Parameters.AddWithValue("@username", trigger_source_username);
    mySqlCommand3.Parameters.AddWithValue("@additional_data", additional_data);
    mySqlCommand3.Parameters.AddWithValue("@type", (int)checkEventType);
    mySqlCommand3.Parameters.AddWithValue("@event_id", skip_event_id);
    return (long?) await mySqlCommand3.ExecuteScalarAsync() > limit;
}

public static async Task CreateReward(MySqlConnection connection, string targetUsername, string trigger_source_username, TransactionDescriptionType descriptionType, int rewardSP, TransactionType transactionType)
{
    MySqlTransaction myTrans = connection.BeginTransaction();
    try
    {
        var mySqlCommand5 = new MySql.Data.MySqlClient.MySqlCommand();
        var availableFrom = DateTime.UtcNow; //.AddDays(1);
        mySqlCommand5.CommandText = "INSERT INTO user_transactions (username,amount, transaction_type,additional_data,description_type,available_from) VALUES(@username, @amount, @transaction_type,@additional_data,@description_type, @available_from)";
        mySqlCommand5.Parameters.AddWithValue("@username", targetUsername);
        mySqlCommand5.Parameters.AddWithValue("@amount", rewardSP);
        mySqlCommand5.Parameters.AddWithValue("@transaction_type", (int)transactionType);
        mySqlCommand5.Parameters.AddWithValue("@additional_data", trigger_source_username);
        mySqlCommand5.Parameters.AddWithValue("@description_type", (int)descriptionType);
        mySqlCommand5.Parameters.AddWithValue("@available_from", availableFrom);
        mySqlCommand5.Connection = connection;
        mySqlCommand5.Transaction = myTrans;
        if ((long)mySqlCommand5.ExecuteNonQuery() > 0)
        {
            var mySqlCommand6 = CreateNotificationMySQLCommand(connection, myTrans,
             targetUsername, trigger_source_username, descriptionType, availableFrom, NotificationType.REWARD);
            if ((long)mySqlCommand6.ExecuteNonQuery() > 0)
            {
                myTrans.Commit();
                await  FirebaseMessagingUtil.GetUserDevicesAndSendDefaultFCMNotification(targetUsername, connection, 0, 1);
            }
            else myTrans.Rollback();
        }
        else myTrans.Rollback();
    }
    catch (Exception)
    {
        myTrans.Rollback();
    }
}
public static MySql.Data.MySqlClient.MySqlCommand CreateNotificationMySQLCommand(MySqlConnection connection, MySqlTransaction? myTrans,
    string targetUsername, string additional_data, TransactionDescriptionType descriptionType, DateTime availableFrom, NotificationType notificationType)
{
    var mySqlCommand6 = new MySql.Data.MySqlClient.MySqlCommand();
    mySqlCommand6.CommandText = "INSERT INTO user_notification (username,type,additional_data, transaction_description_type, available_from) VALUES(@username, @type,@additional_data,@description_type,@available_from)";
    mySqlCommand6.Parameters.AddWithValue("@username", targetUsername);
    mySqlCommand6.Parameters.AddWithValue("@type", notificationType);
    mySqlCommand6.Parameters.AddWithValue("@additional_data", additional_data);
    mySqlCommand6.Parameters.AddWithValue("@description_type", (int)descriptionType);
    mySqlCommand6.Parameters.AddWithValue("@available_from", availableFrom);
    mySqlCommand6.Connection = connection;
    if (myTrans != null) mySqlCommand6.Transaction = myTrans;
    return mySqlCommand6;
}

public static async Task TerminateAccount(ServerSettings settings, MySqlConnection connection, string username)
{
    using (var httpClient = new HttpClient())
    {
        try
        {
            using (var client = Ignition.StartClient(new IgniteClientConfiguration
            {
                Endpoints = new[] { settings.IgniteEndpoint }
            }))
            {

                var generalCache = client.GetOrCreateCache<string, IBinaryObject>("storyarticle");

                var mySqlCommandDeleteLinks = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandDeleteLinks.CommandText = "DELETE FROM sent_links_users WHERE username = @username";
                mySqlCommandDeleteLinks.Connection = connection;
                mySqlCommandDeleteLinks.Parameters.AddWithValue("@username", username);
                await mySqlCommandDeleteLinks.ExecuteNonQueryAsync();

                var mySqlCommandDeleteFCM = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandDeleteFCM.CommandText = "DELETE FROM user_fcm_token WHERE username = @username";
                mySqlCommandDeleteFCM.Connection = connection;
                mySqlCommandDeleteFCM.Parameters.AddWithValue("@username", username);
                await mySqlCommandDeleteFCM.ExecuteNonQueryAsync();

                var mySqlCommandFollowing = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandFollowing.CommandText = "DELETE FROM user_following WHERE following = @username";
                mySqlCommandFollowing.Connection = connection;
                mySqlCommandFollowing.Parameters.AddWithValue("@username", username);
                await mySqlCommandFollowing.ExecuteNonQueryAsync();


                var mySqlCommandFollows = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandFollows.CommandText = "DELETE FROM user_following WHERE username = @username";
                mySqlCommandFollows.Connection = connection;
                mySqlCommandFollows.Parameters.AddWithValue("@username", username);
                await mySqlCommandFollows.ExecuteNonQueryAsync();

                var mySqlCommandInvite = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandInvite.CommandText = "DELETE FROM user_invite WHERE username_source = @username";
                mySqlCommandInvite.Connection = connection;
                mySqlCommandInvite.Parameters.AddWithValue("@username", username);
                await mySqlCommandInvite.ExecuteNonQueryAsync();


                var mySqlCommand = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommand.CommandText = "SELECT slug_title FROM user_stories WHERE username = @username";
                mySqlCommand.Connection = connection;
                mySqlCommand.Parameters.AddWithValue("@username", username);
                List<string> slugTitleToDelete = new List<string>();
                using (var reader = mySqlCommand.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        slugTitleToDelete.Add(reader.GetString("slug_title"));
                    }
                    reader.Close();
                }
                foreach (var toDelete in slugTitleToDelete)
                {
                    var mySqlCommandStoryLogDelete = new MySql.Data.MySqlClient.MySqlCommand();
                    mySqlCommandStoryLogDelete.CommandText = "DELETE FROM user_story_log WHERE slug_title = @slug_title";
                    mySqlCommandStoryLogDelete.Connection = connection;
                    mySqlCommandStoryLogDelete.Parameters.AddWithValue("@slug_title", toDelete);
                    await mySqlCommandStoryLogDelete.ExecuteNonQueryAsync();
                    var deleteRespone = await httpClient.DeleteAsync(settings.GorseAPIEndpoint + "item/" + toDelete);
                    if (!deleteRespone.IsSuccessStatusCode)
                    {
                        Console.WriteLine("Error occured when deleting post '{0}' in GORSE for user {1}, statusCode: {2}, response:" + deleteRespone.Content.ReadAsStringAsync().Result, toDelete, username, deleteRespone.StatusCode);
                        return;
                    }
                    var deleteResult = await generalCache.WithKeepBinary<string, IBinaryObject>().RemoveAsync(toDelete);
                    if (!deleteResult)
                    {
                        Console.WriteLine("Error occured when deleting post '{0}' in Ignite for username {1}", toDelete, username);
                        return;
                    }
                }
                var mySqlCommandStoryDelete = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandStoryDelete.CommandText = "DELETE FROM user_stories WHERE username = @username";
                mySqlCommandStoryDelete.Connection = connection;
                mySqlCommandStoryDelete.Parameters.AddWithValue("@username", username);
                await mySqlCommandStoryDelete.ExecuteNonQueryAsync();

                var mySqlCommandTransactionsDelete = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandTransactionsDelete.CommandText = "DELETE FROM user_transactions WHERE username = @username";
                mySqlCommandTransactionsDelete.Connection = connection;
                mySqlCommandTransactionsDelete.Parameters.AddWithValue("@username", username);
                await mySqlCommandTransactionsDelete.ExecuteNonQueryAsync();

                var mySqlCommandTransferRequestDelete = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandTransferRequestDelete.CommandText = "DELETE FROM claim_transfer_request WHERE transfer_request_id IN (SELECT transfer_request_id FROM user_claimed_rewards WHERE username =@username and transfer_request_id is not null and transfered_at is NULL )";
                mySqlCommandTransferRequestDelete.Connection = connection;
                mySqlCommandTransferRequestDelete.Parameters.AddWithValue("@username", username);
                await mySqlCommandTransferRequestDelete.ExecuteNonQueryAsync();

                var mySqlCommandRewardsDelete = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandRewardsDelete.CommandText = "DELETE FROM user_claimed_rewards WHERE username = @username";
                mySqlCommandRewardsDelete.Connection = connection;
                mySqlCommandRewardsDelete.Parameters.AddWithValue("@username", username);
                await mySqlCommandRewardsDelete.ExecuteNonQueryAsync();

                var mySqlCommandNotificationsDelete = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandNotificationsDelete.CommandText = "DELETE from user_notification WHERE username = @username ";
                mySqlCommandNotificationsDelete.Connection = connection;
                mySqlCommandNotificationsDelete.Parameters.AddWithValue("@username", username);
                await mySqlCommandNotificationsDelete.ExecuteNonQueryAsync();

                var mySqlCommandWalletDelete = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandWalletDelete.CommandText = "DELETE from user_wallet_history WHERE username = @username ";
                mySqlCommandWalletDelete.Connection = connection;
                mySqlCommandWalletDelete.Parameters.AddWithValue("@username", username);
                await mySqlCommandWalletDelete.ExecuteNonQueryAsync();

                var mySqlEmailTerminationNotice = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlEmailTerminationNotice.CommandText = "insert into mail_queue (email,type,additional_data) SELECT email, 6 as type,username as additional_data FROM users where username = @username";
                mySqlEmailTerminationNotice.Parameters.AddWithValue("@username", username);
                mySqlEmailTerminationNotice.Connection = connection;
                await mySqlEmailTerminationNotice.ExecuteNonQueryAsync();


                var mySqlCommandDeleteMessages = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandDeleteMessages.CommandText = "DELETE FROM user_message WHERE username = @username";
                mySqlCommandDeleteMessages.Connection = connection;
                mySqlCommandDeleteMessages.Parameters.AddWithValue("@username", username);
                await mySqlCommandDeleteMessages.ExecuteNonQueryAsync();


                var mySqlCommandRewardsDeleteUser = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandRewardsDeleteUser.CommandText = "DELETE FROM users WHERE username = @username";
                mySqlCommandRewardsDeleteUser.Connection = connection;
                mySqlCommandRewardsDeleteUser.Parameters.AddWithValue("@username", username);
                await mySqlCommandRewardsDeleteUser.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception occured when deleting user {0}, exception: " + ex.Message, username);
        }

    }
} 
}