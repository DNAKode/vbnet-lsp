using System.Collections.Generic;
using System.Linq;

namespace LinqSample
{
    public class Runner
    {
        public IList<int> Execute()
        {
            var numbers = new List<int> { 1, 2, 3 };
            var result = numbers./*caret*/Select(n => n * 2).ToList();
            return result;
        }
    }
}
