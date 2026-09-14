using System.ComponentModel.DataAnnotations;

namespace image_flip_bosch.Backend.Models
{
  public class Meme
  {
    public long Id { get; init; }

    [MaxLength(64)]
    public string User { get; init; } = "";

    [MaxLength(128)]
    public string ContentType { get; init; } = "";

    public byte[] Data { get; init; } = [];

    [MaxLength(64)]
    public string Hash { get; init; } = "";

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public ICollection<Vote> Votes { get; init; } = [];
  }
}
