namespace LaylaHft.Platform.MarketData.Services.Interfaces;

public class CircularBuffer<T>
{
    private readonly T[] _buffer;
    private int _index = 0;
    private bool _isFull = false;

    public CircularBuffer(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentException("Le buffer doit avoir une taille > 0.");

        _buffer = new T[capacity];
    }

    public void Add(T item)
    {
        _buffer[_index] = item;
        _index = (_index + 1) % _buffer.Length;

        if (_index == 0)
            _isFull = true;
    }

    public IReadOnlyList<T> ToList()
    {
        if (_isFull)
        {
            return _buffer
                .Skip(_index)
                .Concat(_buffer.Take(_index))
                .ToList();
        }

        return _buffer.Take(_index).ToList();
    }

    public void Clear()
    {
        Array.Clear(_buffer, 0, _buffer.Length);
        _index = 0;
        _isFull = false;
    }
}
