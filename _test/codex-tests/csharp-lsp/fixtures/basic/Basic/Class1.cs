namespace BasicSample
{
    public class Calculator
    {
        public int Add(int left, int right) => left + right;
    }

    public class UseCalculator
    {
        public int Compute()
        {
            var calc = new Calculator();
            var result = calc./*caret*/Add(1, 2);
            return result;
        }
    }
}
