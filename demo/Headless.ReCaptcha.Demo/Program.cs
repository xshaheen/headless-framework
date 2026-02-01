// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.ReCaptcha;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

var recaptchaV3 = builder.Configuration.GetSection("ReCaptcha:V3");
builder.Services.AddReCaptchaV3(x =>
{
    x.VerifyBaseUrl = recaptchaV3["VerifyBaseUrl"] ?? "https://recaptcha.google.cn/";
    x.SiteKey = recaptchaV3["SiteKey"] ?? "YOUR_RECAPTCHA_V3_SITE_KEY";
    x.SiteSecret = recaptchaV3["SiteSecret"] ?? "YOUR_RECAPTCHA_V3_SITE_SECRET";
});

var recaptchaV2 = builder.Configuration.GetSection("ReCaptcha:V2");
builder.Services.AddReCaptchaV2(x =>
{
    x.VerifyBaseUrl = recaptchaV2["VerifyBaseUrl"] ?? "https://recaptcha.google.cn/";
    x.SiteKey = recaptchaV2["SiteKey"] ?? "YOUR_RECAPTCHA_V2_SITE_KEY";
    x.SiteSecret = recaptchaV2["SiteSecret"] ?? "YOUR_RECAPTCHA_V2_SITE_SECRET";
});

var app = builder.Build();

app.UseDeveloperExceptionPage();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

await app.RunAsync();
