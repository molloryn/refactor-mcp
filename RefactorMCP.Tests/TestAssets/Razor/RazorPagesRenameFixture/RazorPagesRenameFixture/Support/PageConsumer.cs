using RazorPagesRenameFixture.Pages;

namespace RazorPagesRenameFixture.Support;

public static class PageConsumer
{
    public static string Read(IndexModel model) => model.CurrentValue;
}
