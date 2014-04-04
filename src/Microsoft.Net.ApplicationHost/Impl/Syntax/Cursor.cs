namespace Microsoft.Net.ApplicationHost.Impl.Syntax
{
    internal struct Cursor
    {
        private readonly string _text;
        private readonly int _start;
        private readonly int _end;

        public Cursor(string text, int start, int end)
        {
            _text = text;
            _start = start;
            _end = end;
        }

        public bool IsEnd
        {
            get { return _start == _end; }
        }

        public char Peek(int index)
        {
            return (index + _start) >= _end ? (char)0 : _text[index + _start];
        }

        public Result<TValue> Advance<TValue>(TValue result, int length)
        {
            return new Result<TValue>(result, new Cursor(_text, _start + length, _end));
        }
    }
}