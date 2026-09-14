namespace image_flip_bosch.Backend.Models
{
  public record MemeSummary(long Id, string User, string ContentType, DateTime CreatedAt, int Size, int Score, int MyVote);
}
