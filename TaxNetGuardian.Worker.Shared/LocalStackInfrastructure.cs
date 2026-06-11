using System.Net;
using System.Text.Json;

namespace TaxNetGuardian.Worker.Shared;

public sealed class LocalStackSqsQueueClient(WorkerOptions options, HttpClient http) : IQueueClient
{
    public async Task SendAsync(string queueName, QueueEnvelope envelope, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Action"] = "SendMessage",
            ["QueueUrl"] = QueueUrl(queueName),
            ["MessageBody"] = JsonSerializer.Serialize(envelope),
            ["Version"] = "2012-11-05"
        });
        await SendLocalStackAsync(content, cancellationToken);
    }

    public async Task<IReadOnlyList<ReceivedQueueMessage>> ReceiveAsync(string queueName, int maxMessages, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Action"] = "ReceiveMessage",
            ["QueueUrl"] = QueueUrl(queueName),
            ["MaxNumberOfMessages"] = Math.Clamp(maxMessages, 1, 10).ToString(),
            ["WaitTimeSeconds"] = "1",
            ["Version"] = "2012-11-05"
        });
        var xml = await SendLocalStackAsync(content, cancellationToken);
        return ParseMessages(xml);
    }

    public async Task DeleteAsync(string queueName, string receiptHandle, CancellationToken cancellationToken)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Action"] = "DeleteMessage",
            ["QueueUrl"] = QueueUrl(queueName),
            ["ReceiptHandle"] = receiptHandle,
            ["Version"] = "2012-11-05"
        });
        await SendLocalStackAsync(content, cancellationToken);
    }

    private async Task<string> SendLocalStackAsync(HttpContent content, CancellationToken cancellationToken)
    {
        using var response = await http.PostAsync(options.LocalStackEndpoint, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LocalStack SQS call failed: {(int)response.StatusCode} {body}");
        }

        return body;
    }

    private string QueueUrl(string queueName)
        => $"{options.LocalStackEndpoint.TrimEnd('/')}/000000000000/{queueName}";

    private static IReadOnlyList<ReceivedQueueMessage> ParseMessages(string xml)
    {
        var messages = new List<ReceivedQueueMessage>();
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        var messageNodes = doc.Descendants().Where(x => x.Name.LocalName == "Message");
        foreach (var node in messageNodes)
        {
            var body = WebUtility.HtmlDecode(node.Descendants().FirstOrDefault(x => x.Name.LocalName == "Body")?.Value ?? "");
            var receipt = node.Descendants().FirstOrDefault(x => x.Name.LocalName == "ReceiptHandle")?.Value ?? "";
            var envelope = JsonSerializer.Deserialize<QueueEnvelope>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (envelope is not null && !string.IsNullOrWhiteSpace(receipt))
            {
                messages.Add(new ReceivedQueueMessage(envelope, receipt));
            }
        }

        return messages;
    }
}

public sealed class LocalStackS3ObjectStorageClient(WorkerOptions options, HttpClient http) : IObjectStorageClient
{
    public async Task PutObjectAsync(string bucket, string key, string contentType, string content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"{options.LocalStackEndpoint.TrimEnd('/')}/{bucket}/{Uri.EscapeDataString(key).Replace("%2F", "/")}");
        request.Content = new StringContent(content, System.Text.Encoding.UTF8, contentType);
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LocalStack S3 put failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        }
    }

    public async Task<string?> GetObjectAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync($"{options.LocalStackEndpoint.TrimEnd('/')}/{bucket}/{Uri.EscapeDataString(key).Replace("%2F", "/")}", cancellationToken);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
    }
}
