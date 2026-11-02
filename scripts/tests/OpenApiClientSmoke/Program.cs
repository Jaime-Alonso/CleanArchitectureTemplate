using System.Net.Http.Headers;
using Smoke;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Check(typeof(CreateProductRequest).GetProperty(nameof(CreateProductRequest.Price))!.PropertyType == typeof(decimal), "Create price must be decimal.");
Check(typeof(UpdateProductRequest).GetProperty(nameof(UpdateProductRequest.Price))!.PropertyType == typeof(decimal), "Update price must be decimal.");
Check(typeof(ProductDto).GetProperty(nameof(ProductDto.Price))!.PropertyType == typeof(decimal), "Read price must be decimal.");
Check(typeof(ProductDto).GetProperty(nameof(ProductDto.Version))!.PropertyType == typeof(long), "Version must be Int64.");
Check(typeof(UpdateProductRequest).GetProperty(nameof(UpdateProductRequest.ExpectedVersion))!.PropertyType == typeof(long), "ExpectedVersion must be Int64.");

using var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("SMOKE_URL")! + "/") };
var api = new ApiClient(http);
var login = await api.LoginAsync(XApiVersion._1_0, new LoginRequest
{
    Email = "admin@local.template", Password = Environment.GetEnvironmentVariable("SMOKE_PASSWORD")!
});
Check(!string.IsNullOrEmpty(login.AccessToken), "Login returned no access token.");
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
var created = await api.CreateProductAsync(XApiVersion4._1_0, new CreateProductRequest
{
    Name = "Smoke product", Description = "Client smoke", Price = 12345678901234.56m, Stock = 5
});
var product = await api.GetProductByIdAsync(created.Id, XApiVersion8._1_0);
Check(product.Version > 0 && product.Price == 12345678901234.56m, "Initial product values are incorrect.");
long version = product.Version;
await api.UpdateProductAsync(created.Id, XApiVersion6._1_0, new UpdateProductRequest
{
    ExpectedVersion = version, Name = "Updated smoke product", Description = "Client smoke", Price = 98765432109876.54m, Stock = 4
});
var updated = await api.GetProductByIdAsync(created.Id, XApiVersion8._1_0);
Check(updated.Version > version && updated.Price == 98765432109876.54m, "Updated product values are incorrect.");
try
{
    await api.DeleteProductAsync(created.Id, version, XApiVersion7._1_0);
    throw new InvalidOperationException("Stale version was accepted.");
}
catch (ApiException ex) when (ex.StatusCode == 409)
{
}
await api.DeleteProductAsync(created.Id, updated.Version, XApiVersion7._1_0);
try
{
    await api.GetProductByIdAsync(created.Id, XApiVersion8._1_0);
    throw new InvalidOperationException("Deleted product was still found.");
}
catch (ApiException ex) when (ex.StatusCode == 404)
{
    Console.WriteLine("Login, create, read, update, stale-version 409, delete and 404 passed; prices are decimal and versions Int64.");
}
