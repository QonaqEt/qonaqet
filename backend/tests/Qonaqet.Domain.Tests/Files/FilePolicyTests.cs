using System.Text;
using Qonaqet.Domain.Files;

namespace Qonaqet.Domain.Tests.Files;

public sealed class FilePolicyTests
{
    [Theory]
    [InlineData(new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2D, 0x31 }, FilePolicy.Pdf)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, FilePolicy.Png)]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, FilePolicy.Jpeg)]
    public void Binary_types_are_detected_from_magic_bytes(byte[] content, string expected) =>
        Assert.Equal(expected, FileContentSniffer.Detect(content));

    [Fact]
    public void Utf8_text_is_csv_and_binary_garbage_is_unknown()
    {
        Assert.Equal(FilePolicy.Csv, FileContentSniffer.Detect(Encoding.UTF8.GetBytes("ad,email\nLeyla Əliyeva,leyla@example.az\n")));
        Assert.Null(FileContentSniffer.Detect([0x4D, 0x5A, 0x00, 0x01]));
        Assert.Null(FileContentSniffer.Detect([0xC3, 0x28]));
        Assert.Null(FileContentSniffer.Detect([]));
    }

    [Fact]
    public void Every_purpose_has_a_policy_of_at_most_five_megabytes()
    {
        foreach (var purpose in Enum.GetValues<FilePurpose>())
        {
            Assert.True(FilePolicy.For(purpose).MaxSizeBytes <= FilePolicy.FiveMegabytes);
        }

        Assert.Equal([FilePolicy.Pdf, FilePolicy.Jpeg, FilePolicy.Png], FilePolicy.For(FilePurpose.VerificationDocument).ContentTypes);
        Assert.False(FilePolicy.For(FilePurpose.VerificationDocument).IsPublic);
        Assert.True(FilePolicy.For(FilePurpose.Logo).IsPublic);
    }
}
