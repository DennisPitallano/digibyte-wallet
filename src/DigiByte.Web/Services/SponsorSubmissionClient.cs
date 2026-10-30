using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DigiByte.P2P.Shared.Sponsors;

namespace DigiByte.Web.Services;

/// <summary>What the Sponsors form sends. There is nothing wallet-related in it: no addresses, balances or keys.</summary>
public sealed record SponsorSubmission(
    SponsorSubmissionType Type,
    string? Name,
    string? Link,
    string TxId,
    bool Consent,
    byte[]? Image,
    string? ImageContentType,
    string? Website = null);

/// <summary>
/// Posts the Sponsors form to the project's API, which opens a pull request on the sponsors branch for a maintainer to review.
/// Only the form fields are sent, only to the configured URL. Any failure is reported as a state the form can explain, never thrown.
/// See specs/006-sponsors-page/contracts/submission-api.md.
/// </summary>
public sealed class SponsorSubmissionClient
{
    private readonly HttpClient _http;
    private readonly string _submitUrl;

    public SponsorSubmissionClient(HttpClient http, string submitUrl)
    {
        _http = http;
        _submitUrl = submitUrl;
    }

    public async Task<SponsorSubmissionResult> SubmitAsync(SponsorSubmission submission, CancellationToken ct = default)
    {
        try
        {
            using var form = new MultipartFormDataContent
            {
                { new StringContent(submission.Type == SponsorSubmissionType.Remove ? "remove" : "add"), "type" },
                { new StringContent(submission.TxId), "txid" },
                { new StringContent(submission.Consent ? "true" : "false"), "consent" },
                { new StringContent(submission.Website ?? ""), "website" }, // honeypot: real visitors never fill this in
            };
            if (submission.Type == SponsorSubmissionType.Add)
            {
                form.Add(new StringContent(submission.Name ?? ""), "name");
                if (!string.IsNullOrWhiteSpace(submission.Link)) form.Add(new StringContent(submission.Link), "link");
                if (submission.Image is { Length: > 0 })
                {
                    var file = new ByteArrayContent(submission.Image);
                    file.Headers.ContentType = new MediaTypeHeaderValue(submission.ImageContentType ?? "application/octet-stream");
                    form.Add(file, "image", "picture");
                }
            }

            using var response = await _http.PostAsync(_submitUrl, form, ct);
            var text = await response.Content.ReadAsStringAsync(ct);
            try
            {
                var result = JsonSerializer.Deserialize<SponsorSubmissionResult>(text, SponsorJson.Options);
                if (result is not null) return result;
            }
            catch (JsonException) { }

            // No readable answer: the rate-limit status still means something; anything else is "could not be sent".
            return new SponsorSubmissionResult
            {
                State = response.StatusCode == HttpStatusCode.TooManyRequests ? SponsorSubmissionState.RateLimited : SponsorSubmissionState.Unavailable,
            };
        }
        catch (Exception)
        {
            return new SponsorSubmissionResult { State = SponsorSubmissionState.Unavailable };
        }
    }
}
