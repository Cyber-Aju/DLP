using dlp_agent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.IO; // 1. ADD THIS

// 2. FORCE THE APP OUT OF SYSTEM32 SO PROXY CERTS CAN BE CREATED SAFELY!
string safeFolder = @"C:\ProgramData\AerologueDLP";
if (!Directory.Exists(safeFolder)) Directory.CreateDirectory(safeFolder);
Directory.SetCurrentDirectory(safeFolder);

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();