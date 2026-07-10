using System.Threading.Tasks;
using MySql.Data.MySqlClient;

/// <summary>
/// Tracks slug_titles that have been permanently removed, so GetArticle can tell a
/// story that used to exist (410 Gone) apart from one that never did (404 Not Found).
/// </summary>
public static class DeletedStoriesUtil
{
    public static async Task MarkSlugAsDeletedAsync(
        MySqlConnection connection, string slugTitle, MySqlTransaction? transaction = null)
    {
        var cmd = new MySqlCommand();
        cmd.Connection = connection;
        if (transaction != null)
            cmd.Transaction = transaction;
        // Idempotent — HandleRemoveArticle can safely run more than once for the same slug.
        cmd.CommandText = "INSERT INTO deleted_stories (slug_title) VALUES (@slug_title) " +
                           "ON DUPLICATE KEY UPDATE slug_title = slug_title";
        cmd.Parameters.AddWithValue("@slug_title", slugTitle);
        await cmd.ExecuteNonQueryAsync();
    }
}
