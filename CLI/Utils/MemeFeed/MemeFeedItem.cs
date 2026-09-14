namespace image_flip_bosch.CLI.Utils.MemeFeed;

public sealed record MemeFeedItem(long Id, string User, string ContentType, DateTime CreatedAt, int Size, int Score, int MyVote)
{
  public int Score { get; set; } = Score;

  public int MyVote { get; set; } = MyVote;
}
