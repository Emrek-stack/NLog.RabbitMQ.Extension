using System.Collections.Generic;

namespace NLog.RabbitMQ.Extension.Targets
{
    public static class LogLineEx
    {
        public static void EnsureAdt(this LogLine line)
        {
            if (line.Fields == null)
                line.Fields = new Dictionary<string, object>();
            if (line.Tags != null)
                return;
            line.Tags = new HashSet<string>();
        }

        public static void AddField(this LogLine line, string name, object value)
        {
            if (value == null)
                return;
            if (line.Fields == null)
                line.Fields = new Dictionary<string, object>();
            line.Fields.Add(name, value);
        }

        public static void AddTag(this LogLine line, string tag)
        {
            if (tag == null)
                return;
            if (line.Tags == null)
                line.Tags = new HashSet<string>() { tag };
            else
                line.Tags.Add(tag);
        }
    }
}
