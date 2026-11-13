namespace QuadroSense.IntegrationTests;

public class TestFailException : Exception
{
    public TestFailException(string message) : base(message) { }
    public TestFailException(string message, Exception inner) : base(message, inner) { }
}
