# Contract: Outbound URL policy

Applies to the Digi-ID callback target and DigiPay webhook URLs, at write time (webhooks) and at connect time (always).

**URL checks**
- Absolute `https` URL, no userinfo, length at most 2048, port in the allow-list: webhooks `443, 8443, 8080, 8000`; Digi-ID callbacks `443` only.
- Hostname is not an IP literal in a blocked range.

**Resolved IP checks (connect time)** reject: loopback (`127/8`, `::1`), private (`10/8`, `172.16/12`, `192.168/16`, `fc00::/7`), link-local (`169.254/16`, `fe80::/10`, includes cloud metadata `169.254.169.254`), CGNAT (`100.64/10`), unspecified (`0.0.0.0`, `::`), multicast, reserved, and IPv4-mapped IPv6 forms of all of these.

**Behavior**
- Redirects are not followed (a 3xx is a failed delivery).
- DNS result is checked on the socket actually used (`ConnectCallback`), so rebinding cannot change it after validation.
- Development flag `AllowLoopback` permits loopback only.

**Errors**: a stable `400` for registration/update with a short category only (`must use https`, `address not allowed`, `redirects not allowed`). Resolved IPs and DNS details are logged server-side and never returned. Dispatch skips and logs invalid stored URLs without making a request.
