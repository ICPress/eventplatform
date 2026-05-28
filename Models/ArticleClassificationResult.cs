public class ArticleClassificationResult
{
    public string Category { get; set; }
    public List<string> Labels { get; set; }
    public double ConfidenceScore { get; set; }
    public string Summary { get; set; }
}