using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace RefactorMCP.Tests.Tools;

public class RefactoringHelpersEncodingTests : RefactorMCP.Tests.TestBase
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public async Task ReadFileWithEncodingAsync_Utf8Bom_StripsBomFromText()
    {
        const string content = "public class Sample { }\n";
        var filePath = Path.Combine(TestOutputPath, "Utf8Bom.cs");
        await File.WriteAllTextAsync(filePath, content, new UTF8Encoding(true));

        var (text, encoding) = await RefactoringHelpers.ReadFileWithEncodingAsync(filePath);

        Assert.Equal(content, text);
        Assert.Equal(Utf8Bom, encoding.GetPreamble());
    }

    [Fact]
    public async Task WriteFileWithEncodingAsync_Utf8WithoutBom_DoesNotAddBom()
    {
        const string original = "public class Sample { }\n";
        const string updated = "public class Sample { public int Value => 1; }\n";
        var filePath = Path.Combine(TestOutputPath, "Utf8NoBom.cs");
        await File.WriteAllTextAsync(filePath, original, new UTF8Encoding(false));

        var encoding = await RefactoringHelpers.GetFileEncodingAsync(filePath);
        await RefactoringHelpers.WriteFileWithEncodingAsync(filePath, updated, encoding);

        var bytes = await File.ReadAllBytesAsync(filePath);
        Assert.False(bytes.Take(Utf8Bom.Length).SequenceEqual(Utf8Bom));
        Assert.Equal(updated, await File.ReadAllTextAsync(filePath));
    }

    [Fact]
    public async Task WriteFileWithEncodingAsync_Utf8WithBom_PreservesSingleBom()
    {
        const string original = "public class Sample { }\n";
        const string updated = "public class Sample { public int Value => 1; }\n";
        var filePath = Path.Combine(TestOutputPath, "Utf8WithBom.cs");
        await File.WriteAllTextAsync(filePath, original, new UTF8Encoding(true));

        var (_, encoding) = await RefactoringHelpers.ReadFileWithEncodingAsync(filePath);
        await RefactoringHelpers.WriteFileWithEncodingAsync(filePath, updated, encoding);

        var bytes = await File.ReadAllBytesAsync(filePath);
        Assert.True(bytes.Take(Utf8Bom.Length).SequenceEqual(Utf8Bom));
        Assert.False(bytes.Skip(Utf8Bom.Length).Take(Utf8Bom.Length).SequenceEqual(Utf8Bom));
        Assert.Equal(updated, await File.ReadAllTextAsync(filePath));
    }
}
