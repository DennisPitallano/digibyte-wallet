// Run with:
//   DIGIPAY_KEY=dgp_… dotnet run --project sdk/dotnet/examples/CreateSession

using DigiPay;

var apiKey = Environment.GetEnvironmentVariable("DIGIPAY_KEY")
    ?? throw new InvalidOperationException("Set DIGIPAY_KEY");

using var dp = new DigiPayClient(apiKey);

var session = await dp.Sessions.CreateAsync(new CreateSessionRequest
{
    Amount = 5m,
    Label = "Order #1234",
    Memo = "Customer: alice@example.com",
});

Console.WriteLine($"Session ID:   {session.Id}");
Console.WriteLine($"Amount:       {session.Amount} DGB");
Console.WriteLine($"Address:      {session.Address}");
Console.WriteLine($"Expires:      {session.ExpiresAt:O}");
Console.WriteLine($"BIP21 URI:    {session.Uri}");
Console.WriteLine($"Hosted page:  {session.CheckoutUrl}");
