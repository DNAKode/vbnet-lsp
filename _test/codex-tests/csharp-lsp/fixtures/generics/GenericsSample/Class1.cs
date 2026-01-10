namespace GenericsSample
{
    public interface IService<T>
    {
        T Run(T input);
    }

    public class EchoService : IService<string>
    {
        public string Run(string input) => input;
    }

    public class Runner
    {
        public string Execute()
        {
            IService<string> service = new EchoService();
            var result = service./*caret*/Run("ok");
            return result;
        }
    }
}
