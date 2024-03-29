using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<CountBrokerService>();
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAnyOrigin", p => p
        .AllowAnyOrigin()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var app = builder.Build();
app.UseCors("AllowAnyOrigin");

/*
-- IIS (ehr.Sse.com) -> multi deployment of same solutions
        /EMR_SSE_1 -> pool 1
        /EMR_SSE_2 -> pool 2
        /EMR_SSE --> pool
*/

// An Simple GET API endpoint implementation which will be triggered from client side
// Simple Routing 
// EventSource("/sse");
// lambda callback 
app.MapGet("/sse", async (CancellationToken ct, CountBrokerService service, HttpContext clientContext) =>
{
    /* 
        Here clientContext will consists of whole client information to fetch headers 
        and session details being passed from client connection
     */
    // clientContext.Request.Headers.TryGetValue("Auth")

    // setting the response header as even stream so that client will be knowing it's streamable
    clientContext.Response.Headers.Add("Content-Type", "text/event-stream");

    // Retrieving Client Details
    clientContext.Request.Headers.TryGetValue("X-Practice-ID", out var PracticeId);
    clientContext.Request.Headers.TryGetValue("X-User-ID", out var UserId);

    // keep-alive the request to stream futher until unless cancellation token recieved from client side
    while (!ct.IsCancellationRequested)
    {
        // some business logic to prepare data
        // Get Holded Count
        Count? countRecieved = service.WaitForCount();

        // sending the data back to client when connected client match found 
        if (countRecieved != null && 
        PracticeId == countRecieved?.PracticeId.ToString() && UserId == countRecieved?.UserId.ToString())
        {
            await clientContext.Response.WriteAsync($"data: ");
            await JsonSerializer.SerializeAsync(clientContext.Response.Body, countRecieved);
            await clientContext.Response.WriteAsync($"\n\n");
            await clientContext.Response.Body.FlushAsync();
        }
        
        // clearing the count
        service.ResetCount();
    }
});


// Counts Posting API from another 1700+ solutions
app.MapPost("/sse-count-refresh", async (CancellationToken ct, CountBrokerService service, HttpContext clientContext) =>
{
    // Retrieving Client Details
    clientContext.Request.Headers.TryGetValue("X-Practice-ID", out var PracticeId);
    clientContext.Request.Headers.TryGetValue("X-User-ID", out var UserId);

    // Domain, Count, UserId
    Count postedCount = await JsonSerializer.DeserializeAsync<Count>(clientContext.Request.Body);

    // Notifying New Count to the Broker Service
    service.SetCount(new Count(postedCount.Domain, postedCount.count, Convert.ToInt32(PracticeId), Convert.ToInt32(UserId)));
});

app.Run();