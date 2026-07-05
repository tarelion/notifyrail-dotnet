Create a professional README hero image for a GitHub repository.

Image type:
- Raster image
- True 16:9 widescreen hero banner
- Wide horizontal composition, suitable for the top of a README
- Keep all important content inside the center safe area so it can be cropped to 16:9 without cutting text

Subject:
- NotifyRail, a C#/.NET backend that simulates reliable notification delivery

Layout:
- Use a sparse bento-grid hero layout, not a dense infographic
- Fewer modules than a system overview
- Main title area on the left
- Delivery pipeline running horizontally across the center or lower center
- Small stack chip row
- Small validation badge
- Minimal body text

Style:
- pop-laboratory
- Professional gray-white blueprint grid background (#F2F2F2)
- Muted teal/sage green module blocks (#B8D8BE)
- Fluorescent pink accent (#E91E63) only for validation or important emphasis
- Lemon-yellow marker highlight (#FFF200) under the main title or key phrase
- Charcoal technical linework (#2D2926)
- Crisp technical sans-serif typography
- Bold brutalist title
- Coordinate markers and ruler accents are allowed, but keep them subtle

Hard restrictions:
- Do not use Mermaid-style diagram aesthetics
- Do not use generic stock vector art
- Do not use cute characters, mascots, or emoji
- Do not include fake metadata
- Do not show dates, versions, release numbers, serial numbers, barcode values, author names, organization names, or invented labels
- Do not include endpoint paths
- Do not include paragraph text
- Do not invent features
- Do not distort the project name

Required visible text, exact spelling:
- "NotifyRail"
- "Reliable Notification Delivery Backend"
- ".NET"
- "PostgreSQL"
- "EF Core"
- "Docker"
- "xUnit"
- "API"
- "Message"
- "Delivery"
- "PostgreSQL Queue"
- "Worker"
- "Mock Provider"
- "Report"
- "dotnet test"
- "70 passed"
- "0 failed"

Pipeline:
Show this as a clean left-to-right flow with arrows:
"API" -> "Message" -> "Delivery" -> "PostgreSQL Queue" -> "Worker" -> "Mock Provider" -> "Report"

Text hierarchy:
- "NotifyRail" must be very large and readable
- "Reliable Notification Delivery Backend" must be readable under the title
- Pipeline labels must be readable
- Stack labels must be readable
- Validation badge must clearly show "70 passed" and "0 failed"

Composition goal:
At first glance, a GitHub visitor should understand:
1. This is NotifyRail.
2. It is a backend delivery pipeline project.
3. It uses .NET and PostgreSQL.
4. It has tests passing.
