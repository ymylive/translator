using System.Text;
using TextEncoding = System.Text.Encoding;

namespace EGT.Core.Encoding;

public sealed class TextFileCodec
{
  public TextReadResult Read(string path)
  {
    var bytes = File.ReadAllBytes(path);
    if (HasPrefix(bytes, new byte[] { 0xEF, 0xBB, 0xBF }))
    {
      return new TextReadResult(TextEncoding.UTF8.GetString(bytes, 3, bytes.Length - 3), "utf-8-bom");
    }

    if (HasPrefix(bytes, new byte[] { 0xFF, 0xFE }))
    {
      return new TextReadResult(TextEncoding.Unicode.GetString(bytes, 2, bytes.Length - 2), "utf-16-le");
    }

    if (HasPrefix(bytes, new byte[] { 0xFE, 0xFF }))
    {
      return new TextReadResult(TextEncoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2), "utf-16-be");
    }

    return new TextReadResult(TextEncoding.UTF8.GetString(bytes), "utf-8");
  }

  public void Write(string path, string content, string encodingName)
  {
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);

    byte[] bytes = encodingName.ToLowerInvariant() switch
    {
      "utf-8-bom" => new UTF8Encoding(true).GetBytes(content),
      "utf-16-le" => TextEncoding.Unicode.GetBytes(content),
      "utf-16-be" => TextEncoding.BigEndianUnicode.GetBytes(content),
      _ => new UTF8Encoding(false).GetBytes(content)
    };

    File.WriteAllBytes(path, bytes);
  }

  private static bool HasPrefix(byte[] bytes, byte[] prefix)
  {
    if (bytes.Length < prefix.Length)
    {
      return false;
    }

    for (var i = 0; i < prefix.Length; i++)
    {
      if (bytes[i] != prefix[i])
      {
        return false;
      }
    }

    return true;
  }
}

public sealed record TextReadResult(string Content, string EncodingName);
