using System.Net.Http.Headers;
using Smoke;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

Check(typeof(CreateProductRequest).GetProperty(nameof(CreateProductRequest.Price))!.PropertyType == typeof(double), "Create price uses the native number schema.");
Check(typeof(UpdateProductRequest).GetProperty(nameof(UpdateProductRequest.Price))!.PropertyType == typeof(double), "Update price uses the native number schema.");
Check(typeof(ProductDto).GetProperty(nameof(ProductDto.Price))!.PropertyType == typeof(double), "Read price uses the native number schema.");
Check(typeof(ProductDto).GetProperty(nameof(ProductDto.Version))!.PropertyType == typeof(long), "Version must be Int64.");
Check(typeof(UpdateProductRequest).GetProperty(nameof(UpdateProductRequest.ExpectedVersion))!.PropertyType == typeof(long), "ExpectedVersion must be Int64.");

using var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("SMOKE_URL")! + "/") };
var api = new ApiClient(http);
var login = await api.LoginAsync("1.0", new LoginRequest
{
    Email = "admin@local.template", Password = Environment.GetEnvironmentVariable("SMOKE_PASSWORD")!
});
Check(!string.IsNullOrEmpty(login.AccessToken), "Login returned no access token.");
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
var created = await api.CreateProductAsync("1.0", new CreateProductRequest
{
    Name = "Smoke product", Description = "Client smoke", Price = 12.25d, Stock = 5
});
var product = await api.GetProductByIdAsync(created.Id, "1.0");
Check(product.Version > 0 && product.Price == 12.25d, "Initial product values are incorrect.");
long version = product.Version;
await api.UpdateProductAsync(created.Id, "1.0", new UpdateProductRequest
{
    ExpectedVersion = version, Name = "Updated smoke product", Description = "Client smoke", Price = 24.50d, Stock = 4
});
var updated = await api.GetProductByIdAsync(created.Id, "1.0");
Check(updated.Version > version && updated.Price == 24.50d, "Updated product values are incorrect.");
try
{
    await api.DeleteProductAsync(created.Id, version, "1.0");
    throw new InvalidOperationException("Stale version was accepted.");
}
catch (ApiException ex) when (ex.StatusCode == 409)
{
}
await api.DeleteProductAsync(created.Id, updated.Version, "1.0");
try
{
    await api.GetProductByIdAsync(created.Id, "1.0");
    throw new InvalidOperationException("Deleted product was still found.");
}
catch (ApiException ex) when (ex.StatusCode == 404)
{
    Console.WriteLine("Login, create, read, update, stale-version 409, delete and 404 passed; native numeric prices and Int64 versions passed.");
}
