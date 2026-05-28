
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Apache.Ignite.Core;
using Apache.Ignite.Core.Client;
using Apache.Ignite.Core.Client.Cache;
using MySql.Data.MySqlClient;
using Apache.Ignite.Core.Cache.Query;
using Apache.Ignite.Core.Cache.Configuration;
using Apache.Ignite.Core.Cache.Expiry;

public static class ArticleUtil
{


private static ArticleClassificationResult ParseClassification(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new Exception("Empty classification response");

        // Try to extract JSON block if needed
        int start = raw.IndexOf("{");
        int end = raw.LastIndexOf("}");

        if (start >= 0 && end > start)
            raw = raw.Substring(start, end - start + 1);

        return JsonSerializer.Deserialize<ArticleClassificationResult>(
            raw,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }
        ) ?? throw new Exception("Failed to parse classification JSON");
    }


    public static async Task<string> GetCategoryLabelsAsync(HttpClient httpClient, List<string> messages, string apiKey, string apiEndpoint,
       string model = "llama-3.3-70b-versatile")
    {
        var payload = new
        {
            model = model,
            messages = messages.Select(x => new { role = "user", content = x }).ToArray(),
            temperature = 0.2
        };

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        httpClient.DefaultRequestHeaders.Clear();
        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", apiKey);
        var response = await httpClient.PostAsync(apiEndpoint, content);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(responseJson);
        return doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString()!;
    }

        public static async Task UpdateTagRanksAsync(MySqlConnection connectionStory, List<string>? tags)
    {
        if (tags == null || tags.Count == 0) return;
        try
        {
            var updateCmd = new MySql.Data.MySqlClient.MySqlCommand();
            updateCmd.Connection = connectionStory;
            updateCmd.CommandText = "UPDATE story_tags_rank SET tag_rank = tag_rank+1 WHERE tag IN (" +
                string.Join(",", tags.Select(_ => "?")) + ")";
            updateCmd.Parameters.AddRange(tags.Select(x => new MySqlParameter("", x)).ToArray());

            if (await updateCmd.ExecuteNonQueryAsync() == 0)
            {
                var insertCmd = new MySql.Data.MySqlClient.MySqlCommand();
                insertCmd.Connection = connectionStory;
                insertCmd.CommandText = "INSERT INTO story_tags_rank (tag, tag_rank) VALUES (" +
                    string.Join(",1),(", tags.Select(_ => "?")) + ",1)";
                insertCmd.Parameters.AddRange(tags.Select(x => new MySqlParameter("", x)).ToArray());
                await insertCmd.ExecuteNonQueryAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Could not update tag ranks: {0}", ex.Message);
        }
    }

    public static async Task PublishToGorseAsync(
         ICacheClient<string, StorySavedModel> igniteCache,
        ServerSettings serverSettings,
        HttpClient httpClient,
        StorySavedModel storyModel)
    {

        var strb = new StringBuilder((string.IsNullOrEmpty(storyModel.StoryTitle) ? storyModel.EmptyTitle : storyModel.StoryTitle) + "\n");
        strb.Append(storyModel.ContentText);
        var textLabelsRaw = await httpClient.PostAsync(
            serverSettings.SpacyEndpoint + (storyModel.LangCode ?? ""),
            new StringContent(strb.ToString(), Encoding.UTF8, "application/text"));
        var textLabels = await JsonSerializer.DeserializeAsync<StoryTextInfoModel>(
            await textLabelsRaw.Content.ReadAsStreamAsync());

        // 1. Build prompt
        var prompt = serverSettings.ArticleClassificationPromptTemplate
            .Replace("{CONTENT}", strb.ToString());

        var categories = new List<string>();
        var labels = new List<string>();

        ArticleClassificationResult? classification = null;

        if (!string.IsNullOrEmpty(serverSettings.GroqAPIKey))
        {
            var messages = new List<string> { prompt };

            var rawResponse = await GetCategoryLabelsAsync(
                httpClient,
                messages,
                serverSettings.GroqAPIKey,
                serverSettings.GroqAPIEndpoint
            );

            classification = ParseClassification(rawResponse);

            // ✅ Normalize category
            if (!string.IsNullOrWhiteSpace(classification.Category))
            {
                var normalizedCategory = Normalize(classification.Category);
                if (!string.IsNullOrEmpty(normalizedCategory))
                    categories.Add(normalizedCategory);
            }

            // ✅ Normalize AI labels
            if (classification.Labels != null)
            {
                labels.AddRange(
                    classification.Labels
                        .Select(x => Normalize(x))
                        .Where(x => !string.IsNullOrEmpty(x))
                        .Select(x => $"topic:{x}")
                );
            }
        }
        else
        {
            categories.Add("general");
        }

        // ✅ Normalize user tags
        if (storyModel.Tags != null)
        {
            labels.AddRange(
                storyModel.Tags
                    .Select(x => Normalize(x))
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Select(x => $"tag:{x}")
            );
        }

        // ✅ Normalize NLP entities
        if (textLabels?.Entities != null)
        {
            labels.AddRange(
                textLabels.Entities
                    .Select(x => Normalize(x))
                    .Where(x => !string.IsNullOrEmpty(x))
                    .Select(x => x)
            );
        }

        // ✅ Normalize language
        if (!string.IsNullOrEmpty(storyModel.LangCode))
        {
            var lang = Normalize(storyModel.LangCode);
            if (!string.IsNullOrEmpty(lang))
                labels.Add($"lang:{lang}");
        }

        // ✅ Normalize location
        if (!string.IsNullOrEmpty(storyModel.Location))
        {
            var loc = Normalize(storyModel.Location);
            if (!string.IsNullOrEmpty(loc))
                labels.Add($"loc:{loc}");
        }

        // ✅ Normalize author
        if (!string.IsNullOrEmpty(storyModel.AuthorName))
        {
            var author = Normalize(storyModel.AuthorName);
            if (!string.IsNullOrEmpty(author))
                labels.Add($"author:{author}");
        }

        // ✅ Final cleanup
        labels = labels
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Distinct()
            .Take(25)
            .ToList();

        categories = categories
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        // 5. Build Gorse item
        var itemModel = new GorseItemModel
        {
            ItemId = storyModel.SlugTitle,
            IsHidden = false,
            Timestamp = storyModel.Timestamp,
            Categories = categories.ToArray(),
            Labels = new GorseLabels { Topics =  labels.ToArray()}
        };

        var gorseJson = JsonSerializer.Serialize(itemModel);

        // 6. Send to Gorse
        httpClient.DefaultRequestHeaders.Clear();

        var gorseResponse = await httpClient.PostAsync(
            serverSettings.GorseAPIEndpoint + "item",
            new StringContent(gorseJson, Encoding.UTF8, "application/json")
        );

        if (!gorseResponse.IsSuccessStatusCode)
        {
            var error = await gorseResponse.Content.ReadAsStringAsync();
            throw new Exception("Could not publish to Gorse: " + error);
        }

        // 🔥 Reuse classification for DB
        await SaveStoryTagsAsync(
            igniteCache,
            serverSettings,
            storyModel,
            classification,
            textLabels
        );
    }

    public static async Task SaveStoryTagsAsync(
    ICacheClient<string, StorySavedModel> igniteCache,
    ServerSettings serverSettings,
    StorySavedModel story,
    ArticleClassificationResult? classification,
    StoryTextInfoModel? textLabels)
    {
        var tags = new List<(string Tag, string Type)>();

        // 1. AI labels
        if (classification?.Labels != null)
        {
            tags.AddRange(classification.Labels
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => (Normalize(x), "topic")));
        }

        // 2. Category
        if (!string.IsNullOrWhiteSpace(classification?.Category))
        {
            tags.Add((Normalize(classification.Category), "meta"));
            //update/set article category
            story.Category = classification.Category;
            await igniteCache.ReplaceAsync(story.SlugTitle, story);
        }

        // 3. User tags
        if (story.Tags != null)
        {
            tags.AddRange(story.Tags
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => (Normalize(x), "tag")));
        }

        // 4. NLP entities
        if (textLabels?.Entities != null)
        {
            tags.AddRange(textLabels.Entities
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => (Normalize(x), "entity")));
        }

        // 5. Deduplicate + limit
        var finalTags = tags
            .Where(t => !string.IsNullOrWhiteSpace(t.Tag))
            .Distinct()
            .Take(30)
            .ToList();

        if (!finalTags.Any())
            return;

        using var conn = new MySql.Data.MySqlClient.MySqlConnection(serverSettings.MysqlConnectionStoryPop);
        await conn.OpenAsync();
        var cmd = conn.CreateCommand();

        var values = new List<string>();

        for (int i = 0; i < finalTags.Count; i++)
        {
            values.Add($"(@slug{i}, @tag{i}, @type{i})");

            cmd.Parameters.AddWithValue($"@slug{i}", story.SlugTitle);
            cmd.Parameters.AddWithValue($"@tag{i}", finalTags[i].Tag);
            cmd.Parameters.AddWithValue($"@type{i}", finalTags[i].Type);
        }

        cmd.CommandText = $@"
        INSERT IGNORE INTO story_tags (slug_title, tag, tag_type)
        VALUES {string.Join(",", values)};
    ";

        await cmd.ExecuteNonQueryAsync();
    }

    private static string Normalize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return string.Empty;

        // 1. Trim + lowercase
        var value = input.Trim().ToLowerInvariant();

        // 2. Normalize unicode (å → a, é → e, etc.)
        value = value.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();

        foreach (var c in value)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        value = sb.ToString().Normalize(NormalizationForm.FormC);

        // 3. Replace non-alphanumeric with hyphens
        value = Regex.Replace(value, @"[^a-z0-9]+", "-");

        // 4. Remove duplicate hyphens
        value = Regex.Replace(value, @"-+", "-");

        // 5. Trim hyphens from ends
        value = value.Trim('-');

        // 6. Optional: limit length (important for DB index)
        if (value.Length > 50)
            value = value.Substring(0, 50);

        return value;
    }

    // ---------------------------------------------------------------------------
    // Cache helpers
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the storyarticle cache configured with a 14-day create/update expiry.
    /// Access TTL is intentionally left null so reads do not reset the clock.
    /// </summary>
    public static ICacheClient<string, StorySavedModel> GetArticleCacheWithTtl(IIgniteClient client)
    {
        return client.GetOrCreateCache<string, StorySavedModel>("storyarticle")
        .WithExpiryPolicy(
            new ExpiryPolicy(
                create: TimeSpan.FromDays(14),
                update: TimeSpan.FromDays(14),
                access: null)
        );
    }

    /// <summary>
    /// Attempts a cache get; on miss, fetches the latest published snapshot from
    /// user_story_log, deserialises it.  Returns null when the story cannot be found anywhere.
    /// </summary>
    public static async Task<StorySavedModel?> TryGetWithFallbackAsync(
        ICacheClient<string, StorySavedModel> cache,
        string slugTitle,
        MySqlConnection connection)
    {
        var result = await cache.TryGetAsync(slugTitle);
        if (result.Success)
            return result.Value;

        Console.WriteLine("Cache miss for '{0}' — querying user_story_log", slugTitle);

        var cmd = new MySql.Data.MySqlClient.MySqlCommand(
            "SELECT CAST(UNCOMPRESS(story_compressed) AS CHAR) FROM user_story_log " +
            "WHERE slug_title = @slug " + 
            "ORDER BY log_id DESC LIMIT 1",
            connection);
        cmd.Parameters.AddWithValue("@slug", slugTitle);

        object? raw;
        try { raw = await cmd.ExecuteScalarAsync(); }
        catch (Exception ex)
        {
            Console.WriteLine("DB error fetching log row for '{0}': {1}", slugTitle, ex.Message);
            return null;
        }

        if (raw == null || raw == DBNull.Value)
        {
            Console.WriteLine("No publish log row found for '{0}'", slugTitle);
            return null;
        }

        StorySavedModel? story;
        try
        {
            var json = raw is byte[] bytes
                ? Encoding.UTF8.GetString(bytes)
                : raw.ToString()!;
            story = JsonSerializer.Deserialize<StorySavedModel>(json);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Deserialise failed for '{0}': {1}", slugTitle, ex.Message);
            return null;
        }

        if (story == null) return null;

        return story;
    }
}