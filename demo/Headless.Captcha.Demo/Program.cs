// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Captcha;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

// Compose multiple CAPTCHA providers behind one abstraction, bound from the Headless:Captcha:* sections:
//  - Turnstile as the default provider (its tag helpers render the default widget),
//  - reCAPTCHA v3 as a named provider, selected at runtime through ICaptchaProvider.
builder.Services.AddHeadlessCaptcha(captcha =>
    captcha
        .UseTurnstile(builder.Configuration.GetSection("Headless:Captcha:Turnstile"))
        .UseReCaptchaV3("recaptcha", builder.Configuration.GetSection("Headless:Captcha:ReCaptchaV3"))
);

var app = builder.Build();

app.UseDeveloperExceptionPage();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

await app.RunAsync();
