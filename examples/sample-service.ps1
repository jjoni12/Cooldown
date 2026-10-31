# Example service for Cooldown's services.json: prints a made-up usage report in the expected format.
# A real script would ask the service for its numbers instead.
$now = [DateTimeOffset]::UtcNow
@{
    windows = @(
        @{ lengthSeconds = 18000;  usedPercent = 42; resetsAt = $now.AddHours(3).ToString('o') }   # 5-hour limit
        @{ lengthSeconds = 604800; usedPercent = 12; resetsAt = $now.AddDays(4).ToString('o') }    # weekly limit
    )
} | ConvertTo-Json -Depth 4 -Compress
