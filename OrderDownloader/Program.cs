using System.Data;
using System.Data.SQLite;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using DotNetEnv;

namespace OrderDownloader;

class Program
{
    private const string AuthTokenVar = "AUTH_TOKEN";
    private const string ApiKey = "API_KEY";

    static async Task Main(string[] args)
    {
        Env.TraversePath().Load();

        var authToken = GetEnvironmentVariableOrThrow(AuthTokenVar);
        var apiKey = GetEnvironmentVariableOrThrow(ApiKey);

        var baseUri = new Uri("https://api.pcexpress.ca");

        var historicalOrdersUri =
            new Uri(baseUri, "/pcx-bff/api/v1/ecommerce/v2/superstore/customers/historical-orders");
        
        var client = new HttpClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("bearer", authToken);
        client.DefaultRequestHeaders.Referrer = new Uri("https://www.realcanadiansuperstore.ca/");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Add("x-apikey", apiKey);

        var historicalOrders = await client.GetAsync(historicalOrdersUri);
        historicalOrders.EnsureSuccessStatusCode();

        var orderHistory = await historicalOrders.Content.ReadFromJsonAsync<OrderHistory>();
        if (orderHistory != null)
        {
            Console.WriteLine($"Received {orderHistory.offlineOrdersCount} orders");

            await using var connection = await CreateDatabase("orders.sqlite");

            foreach (var order in orderHistory.orderHistory)
            {
                Console.Write($"Retrieving order ID {order.id}...");

                var orderUri = new Uri(baseUri, $"/pcx-bff/api/v1/ecommerce/v2/superstore/customers/historical-orders/{order.id}");
                var orderResponse = await client.GetAsync(orderUri);
                orderResponse.EnsureSuccessStatusCode();

                var orderPayload = await orderResponse.Content.ReadAsStringAsync();
                await InsertOrder(connection, order.id, orderPayload);
                Console.WriteLine("done");
            }

            Console.WriteLine("Retrieving product information for every product across all orders");
            var productIds = await QueryAllProductIdsAcrossOrders(connection);
            Console.WriteLine($"Found {productIds.Count} products");

            const int storeId = 1560;
            var date = DateTime.Today.ToString("ddMMyyyy");
            
            foreach (var id in productIds)
            {
                Console.Write($"Retrieving product ID {id}...");

                var productUri = new Uri(baseUri,
                    $"/pcx-bff/api/v1/products/{id}?lang=en&date={date}&pickupType=STORE&storeId={storeId}&banner=superstore");
                var productResponse = await client.GetAsync(productUri);
                if (productResponse.IsSuccessStatusCode)
                {
                    var productPayload = await productResponse.Content.ReadAsStringAsync();
                    await InsertProduct(connection, id, productPayload);      
                    Console.WriteLine("done");
                }
                else
                {
                    Console.WriteLine(productResponse.StatusCode);
                }
            }
        }

        Console.WriteLine("done");
    }

    private static async Task<SQLiteConnection> CreateDatabase(string fileName)
    {
        var builder = new SQLiteConnectionStringBuilder
        {
            DataSource = fileName,
            Version = 3
        };

        var connection = new SQLiteConnection(builder.ConnectionString);
        await connection.OpenAsync();

        var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        
        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS orders(
                                rowId INTEGER PRIMARY KEY ASC,
                                orderId TEXT NOT NULL UNIQUE,
                                orderBody TEXT NOT NULL
                              );
                              """;
        await command.ExecuteNonQueryAsync();

        command.CommandText = """
                              CREATE TABLE IF NOT EXISTS products(
                              rowId INTEGER PRIMARY KEY ASC,
                              productCode TEXT NOT NULL UNIQUE,
                              productBody TEXT NOT NULL
                              );
                              """;
        await command.ExecuteNonQueryAsync();
        
        return connection;
    }

    private static async Task InsertOrder(SQLiteConnection connection, string orderId, string orderPayload)
    {
        var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = "INSERT INTO orders(orderId, orderBody) VALUES (@orderId, @orderBody) ON CONFLICT(orderId) DO UPDATE SET orderBody=excluded.orderBody;";
        command.Parameters.AddWithValue("@orderId", orderId);
        command.Parameters.AddWithValue("@orderBody", orderPayload);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertProduct(SQLiteConnection connection, string productCode, string productPayload)
    {
        var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = "INSERT INTO products(productCode, productBody) VALUES (@productCode, @productBody) ON CONFLICT(productCode) DO UPDATE SET productBody=excluded.productBody;";
        command.Parameters.AddWithValue("@productCode", productCode);
        command.Parameters.AddWithValue("@productBody", productPayload);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlySet<string>> QueryAllProductIdsAcrossOrders(SQLiteConnection connection)
    {
        const string query = """
                             SELECT DISTINCT
                                 json_extract(value, '$.product.id') AS productId
                             FROM orders o, json_each(o.orderBody, '$.orderDetails.entries');
                             """;

        var command = connection.CreateCommand();
        command.CommandType = CommandType.Text;
        command.CommandText = query;

        var productIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var productId = reader.GetString(reader.GetOrdinal("productId"));
            productIds.Add(productId);
        }

        return productIds;
    }

    private static string GetEnvironmentVariableOrThrow(string variableName) =>
        Environment.GetEnvironmentVariable(variableName) ??
        throw new InvalidOperationException($"environment variable {variableName} is missing");
}