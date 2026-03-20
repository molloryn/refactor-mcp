using Microsoft.AspNetCore.Mvc.RazorPages;

namespace RazorPagesRenameFixture.Pages;

public class IndexModel : PageModel
{
    public string CurrentValue => "Hello from Razor Pages";

    public void OnGet()
    {
    }
}
