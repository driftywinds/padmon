namespace Padmon.Couch;

public static class CouchControlIds
{
    public const string TriggerLeft = "trigger:left";
    public const string TriggerRight = "trigger:right";

    public static string Button(int index) => $"button:{index}";

    public static string Hat(int index, string direction) => $"hat:{index}:{direction}";

    public const string HatUp = "up";
    public const string HatDown = "down";
    public const string HatLeft = "left";
    public const string HatRight = "right";
}
