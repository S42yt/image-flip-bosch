using System.ComponentModel.DataAnnotations;

namespace image_flip_bosch.Backend.Models
{
  public class Vote
  {
    public long Id { get; init; }

    public long MemeId { get; init; }

    public Meme Meme { get; init; } = null!;

    [MaxLength(64)]
    public string User { get; init; } = "";

    public int Value { get; set; }
  }

  public record VoteRequest(string User, int Value);

  public record VoteResult(int Score, int MyVote);
}
