namespace ChronoTravelers.Engine.Content;

/// <summary>Thrown when a ChronoTravelers.Content JSON file is missing, malformed, or references an id that doesn't exist elsewhere in the catalog.</summary>
public sealed class ContentException : Exception
{
    public ContentException(string message) : base(message)
    {
    }

    public ContentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
