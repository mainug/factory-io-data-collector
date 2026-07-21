namespace MesProj.Infrastructure
{
    public sealed class Result
    {
        public bool Succeeded { get; private set; }
        public string Message { get; private set; }

        private Result(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        public static Result Success(string message)
        {
            return new Result(true, message);
        }

        public static Result Fail(string message)
        {
            return new Result(false, message);
        }
    }
}
