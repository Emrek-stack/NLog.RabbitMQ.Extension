using NLog.Config;
using NLog.Layouts;

namespace NLog.RabbitMQ.Extension.Targets
{
  [NLogConfigurationItem]  
  public class Field
  {
    [RequiredParameter]
    public string Name { get; }

    [RequiredParameter]
    public Layout Layout { get; }

    public Field()
      : this(null, null)
    {
    }

    public Field(string name, Layout layout)
    {
      Name = name;
      Layout = layout;
    }
  }
}
