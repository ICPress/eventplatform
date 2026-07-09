using System.Text;
using System.Text.Json;
using Apache.Ignite.Core;
using Apache.Ignite.Core.Binary;
using Apache.Ignite.Core.Client;
using MySql.Data.MySqlClient;

public static class EventProcessor
{
    private static Random rnd = new Random();
    private static string MINIATURE_BITMAP_PREFIX = "m_";
    public static async Task HandleFollowUser(MySqlConnection connection, string targetUsername, EventQueueRow eventRow)
    {
        var mySqlCommand3 = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommand3.CommandText = "SELECT COUNT(*) FROM user_transactions WHERE username = @username and transaction_type= @transaction_type and description_type=@description_type";
        mySqlCommand3.Parameters.AddWithValue("@username", targetUsername);
        mySqlCommand3.Parameters.AddWithValue("@transaction_type", (int)TransactionType.STORY_POINTS_REWARD);
        mySqlCommand3.Parameters.AddWithValue("@description_type", (int)TransactionDescriptionType.FIRST_FOLLOW);
        mySqlCommand3.Connection = connection;
        if ((long?)await mySqlCommand3.ExecuteScalarAsync() == 0)
        {
            await EventUtil.CreateReward(connection, targetUsername, eventRow.trigger_source_username, TransactionDescriptionType.FIRST_FOLLOW, 5, TransactionType.STORY_POINTS_REWARD);
        }
        var mySqlExistingNotifications = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlExistingNotifications.CommandText = "SELECT COUNT(*) FROM user_notification WHERE username = @username and type = @type and additional_data=@additional_data";
        mySqlExistingNotifications.Parameters.AddWithValue("@username", targetUsername);
        mySqlExistingNotifications.Parameters.AddWithValue("@type", (int)NotificationType.FOLLOW_RECEIVED);
        mySqlExistingNotifications.Parameters.AddWithValue("@additional_data", eventRow.trigger_source_username);
        mySqlExistingNotifications.Connection = connection;
        if ((long?)await mySqlExistingNotifications.ExecuteScalarAsync() == 0)
        {
            var notificationCommand = EventUtil.CreateNotificationMySQLCommand(connection, null, targetUsername, eventRow.trigger_source_username,
             TransactionDescriptionType.FOLLOW_RECEIVED, DateTime.UtcNow, NotificationType.FOLLOW_RECEIVED);
            notificationCommand.ExecuteNonQuery();
            await FirebaseMessagingUtil.GetUserDevicesAndSendDefaultFCMNotification(targetUsername, connection, 0, 1, eventRow.trigger_source_username);
        }
    }

    public static async Task HandleUnfollowUser(MySqlConnection connection, EventQueueRow eventRow)
    {
        var mySqlCommand5 = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommand5.CommandText = "SELECT transaction_id FROM user_transactions WHERE username = @username and transaction_type= @transaction_type and description_type=@description_type";
        mySqlCommand5.Parameters.AddWithValue("@username", eventRow.trigger_source_username);
        mySqlCommand5.Parameters.AddWithValue("@transaction_type", (int)TransactionType.STORY_POINTS_REWARD);
        mySqlCommand5.Parameters.AddWithValue("@description_type", (int)TransactionDescriptionType.FIRST_LIKE_SENT_REWARD);
        mySqlCommand5.Connection = connection;
        using (var reader2 = mySqlCommand5.ExecuteReader())
        {
            uint? transactionsToDelete = null;
            while (await reader2.ReadAsync())
            {
                transactionsToDelete = reader2.GetUInt32("transaction_id");
            }
            await reader2.CloseAsync();
            if (transactionsToDelete != null)
            {
                var mySqlCommand6 = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommand6.CommandText = "SELECT transaction_id FROM user_transactions WHERE transaction_id= @transaction_id";
                mySqlCommand6.Parameters.AddWithValue("@transaction_id", transactionsToDelete ?? 0);
                mySqlCommand6.Connection = connection;
                await mySqlCommand6.ExecuteNonQueryAsync();
            }
        }
    }

    public static async Task HandleLike(MySqlConnection connection, EventQueueRow eventRow, ServerSettings settings)
    {
        var mySqlCommand4 = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommand4.CommandText = "SELECT COUNT(*) FROM user_transactions WHERE username = @username and transaction_type= @transaction_type and description_type=@description_type";
        mySqlCommand4.Parameters.AddWithValue("@username", eventRow.trigger_source_username);
        mySqlCommand4.Parameters.AddWithValue("@transaction_type", (int)TransactionType.STORY_POINTS_REWARD);
        mySqlCommand4.Parameters.AddWithValue("@description_type", (int)TransactionDescriptionType.FIRST_LIKE_SENT_REWARD);
        mySqlCommand4.Connection = connection;
        if ((long?)await mySqlCommand4.ExecuteScalarAsync() == 0)
        {
            await EventUtil.CreateReward(connection, eventRow.trigger_source_username, eventRow.trigger_source_username, TransactionDescriptionType.FIRST_LIKE_SENT_REWARD, 5, TransactionType.STORY_POINTS_REWARD);
        }
        string slugTitle = eventRow.additional_data;
        using (MySqlConnection connectionGorse = new MySqlConnection(settings.MysqlConnectionGorse))
        {
            try
            {
                await connectionGorse.OpenAsync();
                var mySqlLikesCommand = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlLikesCommand.CommandText = "SELECT COUNT(*) as CNT FROM feedback WHERE item_id = @slug_title and feedback_type = 'heart'";
                mySqlLikesCommand.Connection = connectionGorse;
                mySqlLikesCommand.Parameters.AddWithValue("@slug_title", slugTitle);
                var likes = (long?)await mySqlLikesCommand.ExecuteScalarAsync();
                var checkLevel = likes;
                var mySqlCommandStoryAuthor = new MySql.Data.MySqlClient.MySqlCommand();
                mySqlCommandStoryAuthor.CommandText = "select username from user_stories WHERE slug_title = @slug_title LIMIT 1";
                mySqlCommandStoryAuthor.Connection = connection;
                mySqlCommandStoryAuthor.Parameters.AddWithValue("@slug_title", eventRow.additional_data);
                var author = (string?)await mySqlCommandStoryAuthor.ExecuteScalarAsync();
                if (likes >= (int)LikeRewardLevels.X5) checkLevel = (int)LikeRewardLevels.X5;
                if (likes >= (int)LikeRewardLevels.X10) checkLevel = (int)LikeRewardLevels.X10;
                if (likes >= (int)LikeRewardLevels.X50) checkLevel = (int)LikeRewardLevels.X50;
                if (likes >= (int)LikeRewardLevels.X100) checkLevel = (int)LikeRewardLevels.X100;
                if (likes >= (int)LikeRewardLevels.X500) checkLevel = (int)LikeRewardLevels.X500;
                if (likes >= (int)LikeRewardLevels.X1000) checkLevel = (int)LikeRewardLevels.X1000;
                if (likes >= (int)LikeRewardLevels.X5000) checkLevel = (int)LikeRewardLevels.X5000;
                if (likes >= (int)LikeRewardLevels.X10000) checkLevel = (int)LikeRewardLevels.X10000;
                if (likes >= (int)LikeRewardLevels.X5)
                {
                    if (author != null)
                    {

                        var transactionKey = slugTitle + ":" + checkLevel.ToString();
                        var mySqlCommand9 = new MySql.Data.MySqlClient.MySqlCommand();
                        mySqlCommand9.CommandText = "SELECT COUNT(*) FROM user_transactions WHERE username = @username and transaction_type= @transaction_type and description_type=@description_type and additional_data = @additional_data";
                        mySqlCommand9.Parameters.AddWithValue("@username", author);
                        mySqlCommand9.Parameters.AddWithValue("@transaction_type", (int)TransactionType.LIKE_RECEIVED);
                        mySqlCommand9.Parameters.AddWithValue("@description_type", (int)TransactionDescriptionType.LIKE_RECEIVED);
                        mySqlCommand9.Parameters.AddWithValue("@additional_data", transactionKey);
                        mySqlCommand9.Connection = connection;
                        if ((long?)await mySqlCommand9.ExecuteScalarAsync() == 0)
                        {
                            await EventUtil.CreateReward(connection, author, transactionKey, TransactionDescriptionType.LIKE_RECEIVED, (int)(checkLevel / 4 * 5), TransactionType.LIKE_RECEIVED);
                        }
                    }

                }
                if (author != null)
                {
                    using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(settings));
                    using MySqlConnection connectionStory = new MySqlConnection(settings.MysqlConnectionStoryPop);
                    await connectionStory.OpenAsync();
                    var articleCache = ArticleUtil.GetArticleCacheWithTtl(client);
                    var article = await ArticleUtil.TryGetWithFallbackAsync(articleCache, slugTitle, connectionStory);
                    if (article != null)
                    {
                        string? articleTitle = (article.StoryTitle != null && article.StoryTitle != "") ? article.StoryTitle : article.EmptyTitle;
                        if (articleTitle != null)
                        {
                            var notificationCommand = EventUtil.CreateNotificationMySQLCommand(connection, null, author, eventRow.trigger_source_username + ":" + articleTitle, TransactionDescriptionType.LIKE_RECEIVED, DateTime.UtcNow, NotificationType.LIKE_RECEIVED);
                            await notificationCommand.ExecuteNonQueryAsync();
                            await FirebaseMessagingUtil.GetUserDevicesAndSendDefaultFCMNotification(author, connection, 0, 1);
                        }
                    }
                }
            }
            finally
            {
                await connectionGorse.CloseAsync();
            }
        }
    }

    public static async Task HandlePublishedArticle(MySqlConnection connection, EventQueueRow eventRow, ServerSettings settings)
    {
        using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(settings));
        using MySqlConnection connection2 = new MySqlConnection(settings.MysqlConnectionStoryPop);
        using var httpClient = new HttpClient();
        try
        {
            await connection2.OpenAsync();
            var articleCache = ArticleUtil.GetArticleCacheWithTtl(client);
            var article = await ArticleUtil.TryGetWithFallbackAsync(articleCache, eventRow.additional_data, connection2);
            if (article == null)
            {
                Console.WriteLine($"Did not find article with slug title {eventRow.additional_data} when processing published article");
                return;
            }
            // Publish directly to Gorse — standard flow
            await ArticleUtil.PublishToGorseAsync(articleCache, settings, httpClient, article);
            await ArticleUtil.UpdateTagRanksAsync(connection2, article.Tags);
            var usersToNotify = new List<string>();
            var mySqlCommand9 = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand9.CommandText = "SELECT profile_icon FROM users WHERE username = @username LIMIT 1";
            mySqlCommand9.Parameters.AddWithValue("@username", eventRow.trigger_source_username);
            mySqlCommand9.Connection = connection;
            var authorProfile = await mySqlCommand9.ExecuteScalarAsync();
            if (authorProfile == DBNull.Value) authorProfile = null;
            if (authorProfile != null)
            {
                var authorImageInfoMetadata = JsonSerializer.Deserialize<ImageInfoMetadata?>((string)authorProfile);
                if (authorImageInfoMetadata?.MinWidth != null && authorImageInfoMetadata?.MinHeight != null)
                {
                    authorProfile = settings.CDNSmallName + MINIATURE_BITMAP_PREFIX + authorImageInfoMetadata.Name;
                }
                else if (authorImageInfoMetadata != null)
                {
                    authorProfile = settings.CDNSmallName + authorImageInfoMetadata.Name;
                }
                else
                {
                    authorProfile = null;
                }
            }
            mySqlCommand9 = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand9.CommandText = "SELECT uf.username FROM user_following uf WHERE uf.following = @followingUsername ";
            mySqlCommand9.Parameters.AddWithValue("@followingUsername", eventRow.trigger_source_username);
            mySqlCommand9.Connection = connection;
            using (var reader2 = mySqlCommand9.ExecuteReader())
            {
                while (await reader2.ReadAsync())
                {
                    var usernameToNotify = reader2.GetString("username");
                    usersToNotify.Add(usernameToNotify);
                }
                await reader2.CloseAsync();
            }
            if (eventRow.additional_data != null)
            {
                string? articleTitle = (article.StoryTitle != null && article.StoryTitle != "") ? article.StoryTitle : article.EmptyTitle;
                if (articleTitle != null)
                {
                    foreach (var user in usersToNotify)
                    {
                        var fcmMessagesToUse = new List<FirebaseAdmin.Messaging.Message>();
                        var devicesToUse = FirebaseMessagingUtil.GetUserDevices(user, connection);
                        foreach (var deviceToken in devicesToUse)
                        {
                            fcmMessagesToUse.Add(FirebaseMessagingUtil.CreateHighPriorityNotificationMessage($"@{eventRow.trigger_source_username} posted {articleTitle}", "Press to read on Storypop", deviceToken, (string?)authorProfile));
                        }
                        await FirebaseMessagingUtil.GetUserDevicesAndSendDefaultFCMNotification(user, connection2, 1, 0, otherMessages: fcmMessagesToUse, devicesToUse: devicesToUse);
                    }
                }

            }


        }
        finally
        {
            await connection2.CloseAsync();
        }


    }

    public static async Task HandleCommentAction(MySqlConnection connection, string targetUsername, EventQueueRow eventRow, ServerSettings settings)
    {
        if (eventRow.type == EventTriggerType.LIKE_COMMENT && await EventUtil.CheckSkipEvent(connection, eventRow.trigger_source_username, eventRow.additional_data, eventRow.event_id, EventTriggerType.LIKE_DELETE)) return;
        var slugTitleCommentUUIDKeyPair = eventRow.additional_data.Split(":");
        if (slugTitleCommentUUIDKeyPair.Length < 2) return;
        string slugTitle = slugTitleCommentUUIDKeyPair[0];
        var commentTriggerUUID = slugTitleCommentUUIDKeyPair[1];
        string? originCommentUUID = null;
        string? targetAuthor = null;
        var mySqlCommand4 = new MySql.Data.MySqlClient.MySqlCommand();
        if (eventRow.type == EventTriggerType.LIKE_COMMENT) mySqlCommand4.CommandText = "SELECT BIN_TO_UUID(reply_to_comment_uuid) as reply_to_comment_uuid, username, reply_to_username FROM user_story_comment WHERE slug_title = @slug_title AND comment_uuid = UUID_TO_BIN(@comment_uuid)";
        else if (eventRow.type == EventTriggerType.WRITE_COMMENT) mySqlCommand4.CommandText = "SELECT null as reply_to_comment_uuid, username, (select username from user_stories where slug_title = @slug_title limit 1) as reply_to_username FROM user_story_comment WHERE slug_title = @slug_title AND comment_uuid = UUID_TO_BIN(@comment_uuid)";
        else mySqlCommand4.CommandText = "SELECT (SELECT @reply_to_comment_uuid FROM user_story_comment  WHERE slug_title = @slug_title AND comment_uuid = UUID_TO_BIN(@reply_to_comment_uuid) LIMIT 1 ) as reply_to_comment_uuid, username, reply_to_username FROM user_story_comment WHERE slug_title = @slug_title AND comment_uuid = UUID_TO_BIN(@comment_uuid)";
        mySqlCommand4.Parameters.AddWithValue("@slug_title", slugTitle);
        mySqlCommand4.Parameters.AddWithValue("@comment_uuid", commentTriggerUUID);
        if (eventRow.type == EventTriggerType.REPLY_COMMENT && slugTitleCommentUUIDKeyPair.Length > 2) mySqlCommand4.Parameters.AddWithValue("@reply_to_comment_uuid", slugTitleCommentUUIDKeyPair[2]);
        mySqlCommand4.Connection = connection;
        using (var readerComment = mySqlCommand4.ExecuteReader())
        {
            while (await readerComment.ReadAsync())
            {
                string? reply_to_comment_uuid = readerComment.IsDBNull(readerComment.GetOrdinal("reply_to_comment_uuid")) ? null : readerComment.GetString("reply_to_comment_uuid");
                if (reply_to_comment_uuid != null) originCommentUUID = reply_to_comment_uuid;
                if (eventRow.type == EventTriggerType.LIKE_COMMENT) targetAuthor = readerComment.GetString("username");
                else targetAuthor = readerComment.IsDBNull(readerComment.GetOrdinal("reply_to_username")) ? null : readerComment.GetString("reply_to_username");
            }
            await readerComment.CloseAsync();
        }
        if (targetAuthor != null)
        {
            if (targetAuthor.Equals(eventRow.trigger_source_username)) return;
            var mySqlCommand9 = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand9.CommandText = "SELECT profile_icon FROM users WHERE username = @username LIMIT 1";
            mySqlCommand9.Parameters.AddWithValue("@username", eventRow.trigger_source_username);
            mySqlCommand9.Connection = connection;
            var authorProfile = await mySqlCommand9.ExecuteScalarAsync();
            if (authorProfile == DBNull.Value) authorProfile = null;
            if (authorProfile != null)
            {
                var authorImageInfoMetadata = JsonSerializer.Deserialize<ImageInfoMetadata?>((string)authorProfile);
                if (authorImageInfoMetadata?.MinWidth != null && authorImageInfoMetadata?.MinHeight != null)
                {
                    authorProfile = settings.CDNSmallName + MINIATURE_BITMAP_PREFIX + authorImageInfoMetadata.Name;
                }
                else if (authorImageInfoMetadata != null)
                {
                    authorProfile = settings.CDNSmallName + authorImageInfoMetadata.Name;
                }
                else
                {
                    authorProfile = null;
                }
            }
            var availableFrom = DateTime.UtcNow;
            var sb = new StringBuilder(eventRow.trigger_source_username);
            sb.Append(":");
            sb.Append(slugTitle);
            sb.Append(":");
            sb.Append(commentTriggerUUID);
            if (originCommentUUID != null)
            {
                sb.Append(":");
                sb.Append(originCommentUUID);
            }
            TransactionDescriptionType transactionType = TransactionDescriptionType.COMMENT_LIKE_REWARD;
            NotificationType notificationType = NotificationType.COMMENT_LIKE_RECEIVED;
            var notificationAction = "";
            switch (eventRow.type)
            {
                case EventTriggerType.REPLY_COMMENT:
                    transactionType = TransactionDescriptionType.COMMENT_REPLY_REWARD;
                    notificationType = NotificationType.COMMENT_REPLY_RECEIVED;
                    notificationAction = "replied to your comment";
                    break;
                case EventTriggerType.LIKE_COMMENT:
                    transactionType = TransactionDescriptionType.COMMENT_LIKE_REWARD;
                    notificationType = NotificationType.COMMENT_LIKE_RECEIVED;
                    break;
                case EventTriggerType.WRITE_COMMENT:
                    transactionType = TransactionDescriptionType.COMMENT_RECEIVED_REWARD;
                    notificationType = NotificationType.COMMENT_RECEIVED;
                    notificationAction = "wrote a comment about your article";
                    break;
                default:
                    break;
            }
            var mySqlCommand6 = EventUtil.CreateNotificationMySQLCommand(connection, null, targetAuthor, sb.ToString(),
            transactionType, availableFrom, notificationType);
            if ((long)await mySqlCommand6.ExecuteNonQueryAsync() > 0)
            {
                var fcmMessagesToUse = new List<FirebaseAdmin.Messaging.Message>();
                var devicesToUse = FirebaseMessagingUtil.GetUserDevices(targetAuthor, connection);
                if (eventRow.type != EventTriggerType.LIKE_COMMENT)
                {
                    foreach (var deviceToken in devicesToUse)
                    {
                        fcmMessagesToUse.Add(FirebaseMessagingUtil.CreateHighPriorityNotificationMessage($"@{eventRow.trigger_source_username} {notificationAction}", "Press to read on Storypop", deviceToken, (string?)authorProfile));
                    }
                }
                await FirebaseMessagingUtil.GetUserDevicesAndSendDefaultFCMNotification(targetUsername, connection, 0, 1, otherMessages: fcmMessagesToUse, devicesToUse: devicesToUse);
            }
        }
    }

    public static DateTime StartOfWeek(DateTime dt, DayOfWeek startOfWeek)
    {
        int diff = (7 + (dt.DayOfWeek - startOfWeek)) % 7;
        return dt.AddDays(-1 * diff).Date;
    }
    public static async Task GenerateWeeklyAwards(string author, MySqlConnection connection)
    {
        var startOfWeekDate = StartOfWeek(DateTime.UtcNow, DayOfWeek.Monday);
        var mySqlCommand9 = new MySql.Data.MySqlClient.MySqlCommand();
        var rewardAmount = rnd?.Next(100, 250);
        var rewardText = "Weekly posts";
        mySqlCommand9.CommandText = "SELECT COUNT(*) FROM user_transactions WHERE username = @username and transaction_type= @transaction_type and description_type=@description_type and  additional_data = @additional_data and created_at >= @date";
        mySqlCommand9.Parameters.AddWithValue("@username", author);
        mySqlCommand9.Parameters.AddWithValue("@transaction_type", (int)TransactionType.STORY_POINTS_REWARD);
        mySqlCommand9.Parameters.AddWithValue("@description_type", (int)TransactionDescriptionType.SPECIAL_REWARD);
        mySqlCommand9.Parameters.AddWithValue("@additional_data", rewardText);
        mySqlCommand9.Parameters.AddWithValue("@date", startOfWeekDate);
        mySqlCommand9.Connection = connection;
        if ((long?)await mySqlCommand9.ExecuteScalarAsync() == 0)
        {
            await EventUtil.CreateReward(connection, author, rewardText, TransactionDescriptionType.SPECIAL_REWARD, rewardAmount ?? 100, TransactionType.STORY_POINTS_REWARD);
        }
    }

    public static async Task HandleRemoveArticle(MySqlConnection connection, EventQueueRow eventRow, ServerSettings settings)
    {
        using var httpClient = new HttpClient();
        try
        {
            var toDelete = eventRow.additional_data;
            using var client = Ignition.StartClient(ConfigUtil.GetIgniteConfiguration(settings));
            var generalCache = ArticleUtil.GetArticleCacheWithTtl(client);
            await generalCache.RemoveAsync(toDelete);
            var mySqlCommandStoryLogDelete = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandStoryLogDelete.CommandText = "DELETE FROM user_story_log WHERE slug_title = @slug_title";
            mySqlCommandStoryLogDelete.Connection = connection;
            mySqlCommandStoryLogDelete.Parameters.AddWithValue("@slug_title", toDelete);
            await mySqlCommandStoryLogDelete.ExecuteNonQueryAsync();

            var deleteRespone = await httpClient.DeleteAsync(settings.GorseAPIEndpoint + "item/" + toDelete);
            if (!deleteRespone.IsSuccessStatusCode)
            {
                Console.WriteLine("Error occured when deleting post '{0}' in GORSE for user {1}, statusCode: {2}, response:" + deleteRespone.Content.ReadAsStringAsync().Result, toDelete, eventRow.trigger_source_username, deleteRespone.StatusCode);
                return;
            }

            var mySqlCommandStoryDelete = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommandStoryDelete.CommandText = "DELETE FROM user_stories WHERE slug_title = @slug_title";
            mySqlCommandStoryDelete.Connection = connection;
            mySqlCommandStoryDelete.Parameters.AddWithValue("@slug_title", toDelete);
            await mySqlCommandStoryDelete.ExecuteNonQueryAsync();

            var mySqlEmailTerminationNotice = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlEmailTerminationNotice.CommandText = "insert into mail_queue (email,type,additional_data) SELECT email, 7 as type,username as additional_data FROM users where username = @username";
            mySqlEmailTerminationNotice.Parameters.AddWithValue("@username", eventRow.trigger_source_username);
            mySqlEmailTerminationNotice.Connection = connection;
            await mySqlEmailTerminationNotice.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error occured when deleting articel:" + ex.Message, ex);
        }
    }

    public static async Task HandleWriteMessage(MySqlConnection connection, string targetUsername, EventQueueRow eventRow, ServerSettings settings)
    {
        var triggerMessageId = eventRow.additional_data.Substring(eventRow.additional_data.IndexOf(":") + 1);
        var mySqlCommand8 = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommand8.CommandText = "DELETE FROM user_notification WHERE username = @username AND notification_read = 1 AND type = 8 AND additional_data LIKE @triggerUsername";
        mySqlCommand8.Parameters.AddWithValue("@username", targetUsername);
        mySqlCommand8.Parameters.AddWithValue("@triggerUsername", eventRow.trigger_source_username + "%");
        mySqlCommand8.Connection = connection;
        await mySqlCommand8.ExecuteNonQueryAsync();
        mySqlCommand8 = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommand8.CommandText = "SELECT COUNT(*) + (select COUNT(*) FROM user_contact_approved WHERE username = @username AND target_username = @triggerUsernamePlain AND blocked = 1) + (select COUNT(*) from user_message WHERE username = @triggerUsernamePlain and target_username = @username and is_read = 1 and message_id >= @triggerMessageId) FROM user_notification WHERE username = @username AND notification_read = 0 AND type = 8 AND additional_data LIKE @triggerUsername";
        mySqlCommand8.Parameters.AddWithValue("@triggerMessageId", triggerMessageId);
        mySqlCommand8.Parameters.AddWithValue("@username", targetUsername);
        mySqlCommand8.Parameters.AddWithValue("@triggerUsernamePlain", eventRow.trigger_source_username);
        mySqlCommand8.Parameters.AddWithValue("@triggerUsername", eventRow.trigger_source_username + "%");
        mySqlCommand8.Connection = connection;
        if ((long?)await mySqlCommand8.ExecuteScalarAsync() == 0) //no unread notifications
        {
            var mySqlCommand9 = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand9.CommandText = "SELECT profile_icon FROM users WHERE username = @username LIMIT 1";
            mySqlCommand9.Parameters.AddWithValue("@username", eventRow.trigger_source_username);
            mySqlCommand9.Connection = connection;
            var authorProfile = await mySqlCommand9.ExecuteScalarAsync();
            if (authorProfile == DBNull.Value) authorProfile = null;
            if (authorProfile != null)
            {
                var authorImageInfoMetadata = JsonSerializer.Deserialize<ImageInfoMetadata?>((string)authorProfile);
                if (authorImageInfoMetadata?.MinWidth != null && authorImageInfoMetadata?.MinHeight != null)
                {
                    authorProfile = settings.CDNSmallName + MINIATURE_BITMAP_PREFIX + authorImageInfoMetadata.Name;
                }
                else if (authorImageInfoMetadata != null)
                {
                    authorProfile = settings.CDNSmallName + authorImageInfoMetadata.Name;
                }
                else
                {
                    authorProfile = null;
                }
            }
            mySqlCommand9 = EventUtil.CreateNotificationMySQLCommand(connection, null, targetUsername, eventRow.trigger_source_username + ":" + eventRow.additional_data.Substring(eventRow.additional_data.IndexOf(":") + 1),
            TransactionDescriptionType.NONE, DateTime.UtcNow, NotificationType.MESSAGE_RECEIVED);
            if ((long)await mySqlCommand9.ExecuteNonQueryAsync() > 0)
            {
                var fcmMessagesToUse = new List<FirebaseAdmin.Messaging.Message>();
                var devicesToUse = FirebaseMessagingUtil.GetUserDevices(targetUsername, connection);
                foreach (var deviceToken in devicesToUse)
                {
                    fcmMessagesToUse.Add(FirebaseMessagingUtil.CreateHighPriorityNotificationMessage($"@{eventRow.trigger_source_username} sent you a message", "Press to read on Storypop", deviceToken, (string?)authorProfile));
                }
                await FirebaseMessagingUtil.GetUserDevicesAndSendDefaultFCMNotification(targetUsername, connection, 0, 1, otherMessages: fcmMessagesToUse, devicesToUse: devicesToUse);
            }
        }
    }

    public static async Task HandleNewMessageNotification(MySqlConnection connection, string targetUsername, EventQueueRow eventRow)
    {
        try
        {
            await FirebaseMessagingUtil.GetUserDevicesAndSendDefaultFCMNotification(targetUsername, connection, 0, 0, messageFromUser: eventRow.trigger_source_username);
            var mySqlCommand8 = new MySql.Data.MySqlClient.MySqlCommand();
            mySqlCommand8.CommandText = "INSERT INTO events_queued (trigger_source_username, additional_data, type,available_from) VALUES(@username, @additional_data, @type,@available_from)";
            mySqlCommand8.Parameters.AddWithValue("@username", eventRow.trigger_source_username);
            mySqlCommand8.Parameters.AddWithValue("@additional_data", eventRow.additional_data);
            mySqlCommand8.Parameters.AddWithValue("@available_from", DateTime.UtcNow.AddSeconds(20));
            mySqlCommand8.Parameters.AddWithValue("@type", (int)EventTriggerType.WRITE_MESSAGE);
            mySqlCommand8.Connection = connection;
            await mySqlCommand8.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error sending FCM message: " + ex.Message);
        }
    }
}