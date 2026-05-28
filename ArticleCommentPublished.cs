public class ArticleCommentPublished
{

    public ArticleCommentPublished(
       string authorName, string slugTitle, string commentUUID,
                               string? reply_to_comment_uuid, string? reply_to_username,
                               string comment,
                               bool hidden,
                               bool deleted,
                               string langCode, string timestamp, bool liked)
    {
        this.authorName = authorName;
        this.comment = comment;
        this.commentUUID = commentUUID;
        this.replyToCommentUUID = reply_to_comment_uuid;
        this.slugTitle = slugTitle;
        this.hidden = hidden;
        this.deleted = deleted;
        this.langCode = langCode;
        this.timestamp = timestamp;
        this.liked = liked;
        this.reply_to_username = reply_to_username;
    }
    public string slugTitle { get; set; } = "";
    public string authorName { get; set; } = "";
    public string comment { get; set; } = "";
    public string commentUUID { get; set; } = "";
    public string? replyToCommentUUID { get; set; } = null;


    public bool hidden { get; set; } = false;

    public bool deleted { get; set; } = false;

    public string langCode { get; set; } = "";

    public uint hearts { get; set; } = 0u;

    public uint numReplies { get; set; } = 0u;
    public List<ArticleCommentPublished> replies { get; set; } = new List<ArticleCommentPublished>();
    public string? authorBadge { get; set; } = null;

    public string? timestamp { get; set; } = null;

    public bool liked { get; set; } = false;

    public string? reply_to_username { get; set; } = null;
}