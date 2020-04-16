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

| Parameter | Description | Value
| --- | --- | --- |
| PLogType | The type of logger you are initiating | file or database |
| PLogEnabled | Enable logging | true or false
| BaseNameFile | The name of the file to log to | applog.txt
| PLogFileMaxSizeKB | The maximum size of a log file before starting a new file | 1024 |
| PLogFileMessageTemplate | A String.Format message template | "Loge Entry: {0}" |
| StringConnection | The string connection to your logging database | "Server=myServerAddress;Database=myDataBase;User Id=myUsername;Password=myPassword;" |
| StoredProcedureName | The Sproc to use when logging to database | "SprocName" |
| MessageParameterName | The Sproc Paramter Name | "@MyParamter" |
| PLogDeleteFilesOlderThanDays | Delete old log files | 0 to not delete. Otherwise number of days |
| PLogStopLoggingIfSpaceSmallerThanMB | Stop if disk is full | 0 if dont stop. Otherwise a long|