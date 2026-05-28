using System.Text.Json.Serialization;
using System.Collections.Generic;

public class StoryTextInfoModel
{
    [JsonPropertyName("nouns")]
    public List<string> Nouns { get; set; } = new List<string>();

    [JsonPropertyName("verbs")]
    public List<string> Verbs { get; set; } = new List<string>();

    [JsonPropertyName("entities")]
    public List<string> Entities { get; set; } = new List<string>();
}
