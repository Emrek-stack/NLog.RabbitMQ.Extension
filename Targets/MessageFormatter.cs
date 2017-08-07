using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Newtonsoft.Json;
using NLog.Layouts;

namespace NLog.RabbitMQ.Extension.Targets
{
    public static class MessageFormatter
    {
        private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, 0);
        private static string _hostName;

        private static string HostName
        {
            get
            {
                return _hostName = _hostName ?? Dns.GetHostName();
            }
        }

        public static string GetMessageInner(bool useJson, Layout layout, LogEventInfo info, IList<Field> fields)
        {
            return GetMessageInner(useJson, false, layout, info, fields);
        }

        public static string GetMessageInner(bool useJson, bool useLayoutAsMessage, Layout layout, LogEventInfo info, IList<Field> fields)
        {
            if (!useJson)
                return layout.Render(info);
            LogLine line = new LogLine()
            {
                TimeStampIso8601 = info.TimeStamp.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture),
                Message = !useLayoutAsMessage ? info.FormattedMessage : layout.Render(info),
                Level = info.Level.Name,
                Type = "amqp",
                Source = new Uri($"nlog://{HostName}/{info.LoggerName}")
            };
            line.AddField("exception", info.Exception);
            if (info.Properties.Count > 0 && info.Properties.ContainsKey("fields"))
            {
                foreach (var keyValuePair in (IEnumerable<KeyValuePair<string, object>>)info.Properties["fields"])
                    line.AddField(keyValuePair.Key, keyValuePair.Value);
            }
            if (info.Properties.Count > 0 && info.Properties.ContainsKey("tags"))
            {
                foreach (string tag in (IEnumerable<string>)info.Properties["tags"])
                    line.AddTag(tag);
            }
            foreach (KeyValuePair<object, object> property in info.Properties)
            {
                switch (property.Key as string)
                {
                    case "tags":
                    case "fields":
                    case null:
                        continue;
                    default:
                        line.AddField((string)property.Key, property.Value);
                        continue;
                }
            }
            if (fields != null)
            {
                foreach (Field field in fields)
                {                                      
                    if (line.Fields == null || !line.Fields.Any())
                    {
                        line.AddField(field.Name, field.Layout.Render(info));
                    }
                }
            }
            line.EnsureAdt();
            return JsonConvert.SerializeObject(line);
        }

        public static long GetEpochTimeStamp(LogEventInfo @event)
        {
            return Convert.ToInt64((@event.TimeStamp - Epoch).TotalSeconds);
        }
    }
}
