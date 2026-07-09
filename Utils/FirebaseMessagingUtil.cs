using FirebaseAdmin.Messaging;
using MySql.Data.MySqlClient;

public static class FirebaseMessagingUtil
{

    public static List<string> GetUserDevices(string usernameToNotify, MySqlConnection newConnection)
    {
        var userDevices = new List<string>();
        var mySqlCommandDevices = new MySql.Data.MySqlClient.MySqlCommand();
        mySqlCommandDevices.CommandText = "SELECT token FROM user_fcm_token WHERE username = @username";
        mySqlCommandDevices.Parameters.AddWithValue("@username", usernameToNotify);
        mySqlCommandDevices.Connection = newConnection;
        using (var readerDevice = mySqlCommandDevices.ExecuteReader())
        {
            while (readerDevice.Read())
            {
                string device = readerDevice.GetString("token");
                userDevices.Add(device);
            }
        }
        return userDevices;
    }

    public static async Task GetUserDevicesAndSendDefaultFCMNotification(string usernameToNotify, MySqlConnection newConnection,
             int newFollowingAddonNotification, int ordinaryNotificationAddon, string? newUserFollowing = null, List<Message>? otherMessages = null, List<string>? devicesToUse = null, string? messageFromUser = null)
    {
        var userDevices = devicesToUse ?? GetUserDevices(usernameToNotify, newConnection);
        if (userDevices.Count > 0)
        {
            var messages = CreateSilentAddonNotificationMessages(userDevices, newFollowingAddonNotification, ordinaryNotificationAddon, messageFromUser);
            if (newUserFollowing != null)
            {
                foreach (string deviceToken in userDevices)
                {
                    messages.Add(CreateHighPriorityNotificationMessage("You have a new follower", "@" + newUserFollowing + " has started following you!", deviceToken));
                }
            }
            if (otherMessages != null) messages.AddRange(otherMessages);
            await SendFCMMultipleDeviceNotification(messages);
        }
    }

    public static FirebaseAdmin.Messaging.Message CreateHighPrioritySilentMessage(Dictionary<string, string> data, string tokenRecipient)
    {
        // See documentation on defining a message payload.
        return new FirebaseAdmin.Messaging.Message()
        {

            Data = data,
            Token = tokenRecipient,
            Apns = new ApnsConfig()
            {
                Aps = new Aps()
                {
                    ContentAvailable = true
                }
            },
            Android = new AndroidConfig()
            {
                TimeToLive = TimeSpan.FromHours(1d),
                Priority = Priority.High,
            }
        };
    }

    public static FirebaseAdmin.Messaging.Message CreateHighPriorityNotificationMessage(string title, string body, string tokenRecipient, string? profileIconPath = null)
    {
        // See documentation on defining a message payload.
        return new FirebaseAdmin.Messaging.Message()
        {

            Notification = new Notification() { Title = title, Body = body, ImageUrl = profileIconPath ?? "https://storypop.net/storypop_ipone.png" },
            Token = tokenRecipient,
            Android = new AndroidConfig()
            {
                TimeToLive = TimeSpan.FromHours(1d),
                Priority = Priority.High,
            }
        };
    }

    public static async Task SendFCMNotification(int newFollowingNotifications, int newNotifications, string tokenRecipient)
    {
        try
        {
            var fcmNotification = new Dictionary<string, string>();
            fcmNotification.Add("newFollowingNotification", newFollowingNotifications.ToString());
            fcmNotification.Add("newNotification", newNotifications.ToString());
            var message = CreateHighPrioritySilentMessage(fcmNotification, tokenRecipient);
            // Send a message to the device corresponding to the provided
            // registration token.
            string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
            // Response is a message ID string.
            Console.WriteLine("Successfully sent FCM message: " + response);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error sending FCM message: " + ex.Message);
        }

    }

    public static List<FirebaseAdmin.Messaging.Message> CreateSilentAddonNotificationMessages(List<string> tokenRecipients, int newFollowingNotifications, int newNotifications, string? messageFromUser = null)
    {
        var fcmNotification = new Dictionary<string, string>();
        fcmNotification.Add("newFollowingNotification", newFollowingNotifications.ToString());
        fcmNotification.Add("newNotification", newNotifications.ToString());
        if (messageFromUser != null) fcmNotification.Add("messageFromUser", messageFromUser);
        var fcmMessages = new List<FirebaseAdmin.Messaging.Message>();
        foreach (var deviceToken in tokenRecipients)
        {
            fcmMessages.Add(CreateHighPrioritySilentMessage(fcmNotification, deviceToken));
        }
        return fcmMessages;
    }

    public static async Task SendFCMMultipleDeviceNotification(List<FirebaseAdmin.Messaging.Message> fcmMessages)
    {
        try
        {
            // Send a message to the device corresponding to the provided
            // registration token.
            var successCount = 0;
            foreach (var message in fcmMessages)
            {
                var result = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                if (result.Length > 0) successCount++;
            }
            // Response is a message ID string.
            Console.WriteLine("Successfully sent FCM messages: "+ successCount.ToString() + ", failed:" + (fcmMessages.Count - successCount).ToString());
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error sending FCM message: " + ex.Message);
        }

    }
}