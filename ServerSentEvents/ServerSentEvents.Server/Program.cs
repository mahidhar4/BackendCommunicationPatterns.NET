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
    int PracticeId = Convert.ToInt32(clientContext.Request.Headers["X-Practice-ID"]);
    int UserId = Convert.ToInt32(clientContext.Request.Headers["X-User-ID"]);
    Guid connectionId = Guid.NewGuid();

    // keep-alive the request to stream futher until unless cancellation token recieved from client side
    while (!ct.IsCancellationRequested)
    {
        // sleeping for 10 secs so that front end will be notified
        Thread.Sleep(10000);

        // some business logic to prepare data
        // Get Holded Count
        List<Count> countsByPractice = brokerService.GetCountIfAvailaible(PracticeId, connectionId);

        // sending the data back to client when connected client match found 
        if (countsByPractice != null)
        {
            foreach (Count count in countsByPractice)
            {
                if (count.UserId == UserId || count.UserId == null || count.UserId == 0)
                {
                    // Custom Events 
                    // await clientContext.Response.WriteAsync($"event: " + count.Domain);
                    // await clientContext.Response.WriteAsync($"\n\n");
                    await clientContext.Response.WriteAsync($"data: ");
                    await JsonSerializer.SerializeAsync(clientContext.Response.Body, count);
                    await clientContext.Response.WriteAsync($"\n\n");
                }
                // clearing the count even if the user connected or not
                brokerService.ResetCount(count);
            }
            // write at once to client
            await clientContext.Response.Body.FlushAsync();
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
    PostCount postedCount = await JsonSerializer.DeserializeAsync<PostCount>(clientContext.Request.Body);

    if (postedCount != null)
    {
        List<Count> preparedList = new List<Count>();
        //inserting all the records of counts for different users
        foreach (int ToUserId in postedCount.UserIds)
        {
            Count prepared = new Count(postedCount.Domain, postedCount.count, PracticeId, ToUserId, DateTime.Now, Guid.NewGuid());
            preparedList.Add(prepared);
        }

        // Notifying New Count to the Broker Service
        brokerService.SetCount(preparedList);
        // send some response back to client to notify it's success
        await JsonSerializer.SerializeAsync(clientContext.Response.Body, new { Message = "Success" });
    }

}).WithName("SSE-REFRESH")
.WithOpenApi();

app.Run();