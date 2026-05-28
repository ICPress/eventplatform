public class CommentLikeReplyNotification
{
    public CommentLikeReplyNotification(ArticleCommentPublished comment, ArticleCommentPublished? commentReply, string triggerUsername)
    {
        this.comment = comment;
        this.commentReply = commentReply;
        this.triggerUsername = triggerUsername;
    }
    public ArticleCommentPublished? commentReply { get; set; }

    public ArticleCommentPublished comment { get; set; }

    public string triggerUsername { get; set; }
}