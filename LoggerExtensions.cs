using System;
using System.Collections.Generic;

namespace NLog.RabbitMQ.Extension
{
    public static class LoggerExtensions
    {
        public static void FatalTag(this Logger logger, string message, params string[] tags)
        {
            Log(logger, LogLevel.Fatal, message, tags);
        }

        public static void ErrorTag(this Logger logger, string message, params string[] tags)
        {
            Log(logger, LogLevel.Error, message, tags);
        }

        public static void WarnTag(this Logger logger, string message, params string[] tags)
        {
            Log(logger, LogLevel.Warn, message, tags);
        }

        public static void InfoTag(this Logger logger, string message, params string[] tags)
        {
            Log(logger, LogLevel.Info, message, tags);
        }

        public static void DebugTag(this Logger logger, string message, params string[] tags)
        {
            Log(logger, LogLevel.Debug, message, tags);
        }

        public static void TraceTag(this Logger logger, string message, params string[] tags)
        {
            Log(logger, LogLevel.Trace, message, tags);
        }

        private static void Log(Logger logger, LogLevel level, string message, string[] tags)
        {
            logger.Log(level, message, (object[])null, null as Exception, tags, (IDictionary<string, object>)null);
        }

        public static void LogField(this Logger logger, LogLevel level, string message, string key, object value)
        {
            if (key == null)
                throw new ArgumentNullException("key");
            logger.Log(level, message, (object[])null, (Exception)null, (string[])null, (IDictionary<string, object>)new Dictionary<string, object>()
      {
        {
          key,
          value
        }
      });
        }

        public static void LogFields<T>(this Logger logger, LogLevel level, string message, params Tuple<string, T>[] fields)
        {
            Dictionary<string, object> dictionary = new Dictionary<string, object>(fields.Length);
            foreach (Tuple<string, T> field in fields)
            {
                if (field.Item1 == null)
                    throw new ArgumentNullException("fields", $"LogFields contains tuple with null key, for message '{message}'");
                dictionary.Add(field.Item1, field.Item2);
            }
            logger.Log(level, message, (object[])null, (Exception)null, (string[])null, (IDictionary<string, object>)dictionary);
        }

        public static void Log(this Logger logger, LogLevel level, string message, object[] parameters = null, Exception exception = null, string[] tags = null, IDictionary<string, object> fields = null)
        {
            if (message == null)
                return;
            LogEventInfo logEventInfo = new LogEventInfo(level, logger.Name, null, message, parameters, exception);
            if (fields != null)
                logEventInfo.Properties.Add("fields", fields);
            if (tags != null)
                logEventInfo.Properties.Add("tags", tags);
            logger.Log(typeof(LoggerExtensions), logEventInfo);
        }
    }
}
