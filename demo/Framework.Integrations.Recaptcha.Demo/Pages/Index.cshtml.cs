// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Framework.Integrations.Recaptcha.Demo.Pages;

internal sealed class IndexModel : PageModel
{
    public PageResult OnGet()
    {
        return Page();
    }
}
