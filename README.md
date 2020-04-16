# InvertedSoftware.PLogger.Core
 Implementation of PLogger As a Logging Provider for .NET Core

To add a PLogger provide to your application:

Add: ```loggerFactory.AddPLogger(Configuration);```
In: ```public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)```

Sample appsettings.json
```
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    },
    "PLogger": {
      "PLogType": "file",
      "PLogEnabled": true,
      "BaseNameFile": "plogger.txt",
      "PLogFileMaxSizeKB": 1024,
      "PLogFileMessageTemplate": "{0}",
      "StringConnection": "StringConnection",
      "StoredProcedureName": "AddError",
      "MessageParameterName": "@ErrorName"
    }
  },
  "AllowedHosts": "*"
}
```