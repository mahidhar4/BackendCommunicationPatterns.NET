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
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseCors("AllowAnyOrigin");

// configure exception middleware
app.UseStatusCodePages(async statusCodeContext
    => await Results.Problem(statusCode: statusCodeContext.HttpContext.Response.StatusCode)
        .ExecuteAsync(statusCodeContext.HttpContext));

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

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
app.MapGet("/sse", async (CancellationToken ct, CountBrokerService brokerService, HttpContext clientContext) =>
{
    /* 
        Here clientContext will consists of whole client information to fetch headers 
        and session details being passed from client connection
     */
    // clientContext.Request.Headers.TryGetValue("Auth")

    // setting the response header as even stream so that client will be knowing it's streamable
    clientContext.Response.Headers.Add("Content-Type", "text/event-stream");

    // Retrieving Client Details
    var PracticeId = clientContext.Request.Headers["X-Practice-ID"];
    var UserId = clientContext.Request.Headers["X-User-ID"];

    // keep-alive the request to stream futher until unless cancellation token recieved from client side
    while (!ct.IsCancellationRequested)
    {
        // sleeping for 5 secs
        Thread.Sleep(5000);

        // some business logic to prepare data
        // Get Holded Count
        Count? countRecieved = brokerService.GetCountIfAvailaible();

        // sending the data back to client when connected client match found 
        if (countRecieved != null)
        {
            // Custom Events 
            await clientContext.Response.WriteAsync($"event: " + countRecieved.Domain);
            await clientContext.Response.WriteAsync($"\n\n");
            await clientContext.Response.WriteAsync($"data: ");
            await JsonSerializer.SerializeAsync(clientContext.Response.Body, countRecieved);
            await clientContext.Response.WriteAsync($"\n\n");
            await clientContext.Response.Body.FlushAsync();
            // clearing the count
            brokerService.ResetCount();
        }
    }
}).WithName("SSE")
.WithOpenApi();


// Counts Posting API from another 1700+ solutions
app.MapPost("/sse-count-refresh", async (CancellationToken ct, CountBrokerService brokerService, HttpContext clientContext) =>
{
    // Retrieving Client Details
    int PracticeId = Convert.ToInt32(clientContext.Request.Headers["X-Practice-ID"]);
    int UserId = Convert.ToInt32(clientContext.Request.Headers["X-User-ID"]);

    // Domain, Count, UserId --> Count Details
    Count postedCount = await JsonSerializer.DeserializeAsync<Count>(clientContext.Request.Body);

    Count prepared = new Count(postedCount.Domain, postedCount.count, 
    postedCount.PracticeId, postedCount.UserId);

    // Notifying New Count to the Broker Service
    brokerService.SetCount(prepared);

     await JsonSerializer.SerializeAsync(clientContext.Response.Body, prepared);
}).WithName("SSE-REFRESH")
.WithOpenApi();


// example 1
app.MapGet("/", () => "Hello, World!");

app.Run();