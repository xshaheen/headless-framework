// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Framework.Integrations.Recaptcha;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddReCaptchaV3(x =>
{
    x.VerifyBaseUrl = "https://recaptcha.google.cn/";
    x.SiteKey = "6LccrsMUAAAAANSAh_MCplqdS9AJVPihyzmbPqWa";
    x.SiteSecret = "6LccrsMUAAAAAL91ysT6Nbhk4MnxpHjyJ_pdVLon";
});

builder.Services.AddReCaptchaV2(x =>
{
    x.VerifyBaseUrl = "https://recaptcha.google.cn/";
    x.SiteKey = "6LcArsMUAAAAAKCjwCTktI3GRHTj98LdMEI9f9eQ";
    x.SiteSecret = "6LcArsMUAAAAAO_FBbZghC9aUa1F1rjvcdiOESKd";
});

var app = builder.Build();

app.UseDeveloperExceptionPage();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();
app.MapRazorPages();

await app.RunAsync();
