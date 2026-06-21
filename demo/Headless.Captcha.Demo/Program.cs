// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Compose multiple CAPTCHA providers behind one abstraction:
//  - Turnstile as the default provider (its tag helpers render the default widget),
//  - reCAPTCHA v3 as a named provider, selected at runtime through ICaptchaProvider.
builder.Services.AddHeadlessCaptcha(captcha =>
    captcha
        .UseTurnstile(options =>
        {
            options.SiteKey = builder.Configuration["Headless:Captcha:Turnstile:SiteKey"] ?? "YOUR_TURNSTILE_SITE_KEY";
            options.SiteSecret =
                builder.Configuration["Headless:Captcha:Turnstile:SiteSecret"] ?? "YOUR_TURNSTILE_SITE_SECRET";
        })
        .UseReCaptchaV3(
            "recaptcha",
            options =>
            {
                options.SiteKey =
                    builder.Configuration["Headless:Captcha:ReCaptchaV3:SiteKey"] ?? "YOUR_RECAPTCHA_V3_SITE_KEY";
                options.SiteSecret =
                    builder.Configuration["Headless:Captcha:ReCaptchaV3:SiteSecret"] ?? "YOUR_RECAPTCHA_V3_SITE_SECRET";
            }
        )
);

var app = builder.Build();

app.UseDeveloperExceptionPage();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

await app.RunAsync();
