// calc-worker. B9 puts the stdio loop here. For now `--eval` proves Natural is linked in: it prints 1/3 at
// 256 bits whatever the expression, and B4 makes it evaluate.

if (args is ["--eval", _])
{
    Console.WriteLine(Calc.Engine.Link.OneThird());
    return 0;
}

Console.Error.WriteLine("usage: calc-worker --eval <expression>");
return 2;
