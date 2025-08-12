using FixedRatioStressTest.Core.Interfaces;
using FixedRatioStressTest.Core.Services;
using FixedRatioStressTest.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// App services  
builder.Services.AddSingleton<IStorageService, JsonFileStorageService>();
builder.Services.AddSingleton<ISolanaClientService, SolanaClientService>();
builder.Services.AddSingleton<ITransactionBuilderService, TransactionBuilderService>();
builder.Services.AddSingleton<IThreadManager, ThreadManager>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();

app.Run();
