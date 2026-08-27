namespace Parkett.Domain;

public enum OrderSide
{
    Buy,
    Sell,
}

public enum OrderType
{
    Market,
    Limit,
    Stop,
}

public enum OrderStatus
{
    New,
    Filled,
    Cancelled,
    Rejected,
}
