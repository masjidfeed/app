# Masjid App REST API

## Overview

The Masjid App plugin exposes a public, read-only WordPress REST API for mobile
clients. No authentication is required.

```text
{siteUrl}/wp-json/masjid/v1
```

All endpoints accept `GET` requests and return JSON. Dates and date-times use
ISO 8601 unless an endpoint explicitly states otherwise. HTML fields contain
rendered WordPress content and must be treated as HTML by clients.

## Client Identification

Every request must identify the client with a `User-Agent` header using exactly
the `appname/version` format. The name and version must each begin with a letter
or digit, contain no spaces or additional slashes, and be at most 100
characters. For example:

```http
User-Agent: MasjidApp-iOS/2.4.1
```

An absent or invalid value returns `400` with error code
`masjidapp_invalid_user_agent`. CORS preflight requests are exempt. When API
tracing is enabled, the server records the parsed application name and version
with the request trace.

For the local Docker environment:

```sh
BASE_URL='http://localhost:8000/wp-json/masjid/v1'
```

For another deployment, replace the origin:

```sh
BASE_URL='https://example.org/wp-json/masjid/v1'
```

## Errors

Errors use the standard WordPress REST error shape:

```json
{
  "code": "masjidapp_post_not_found",
  "message": "Post not found.",
  "data": {
    "status": 404
  }
}
```

| Status | Meaning |
| --- | --- |
| `200` | Request succeeded. |
| `304` | Cached representation is still current. See [ETag caching](#etag-caching). |
| `400` | The `User-Agent` header is missing or invalid. |
| `404` | The requested post does not exist or is not public. |
| `503` | A plugin required to provide the requested data is inactive. |

## Configuration

### `GET /config`

Returns branding, contact details, endpoint discovery URLs, and feature flags.

```sh
curl --fail-with-body --silent --show-error \
  --user-agent 'MasjidApp-iOS/2.4.1' \
  "$BASE_URL/config"
```

Example response:

```json
{
  "masjid": {
    "id": "islamic-center-of-bothell",
    "name": "Islamic Center of Bothell",
    "timezone": "America/Los_Angeles"
  },
  "branding": {
    "logoUrl": "https://example.org/wp-content/uploads/logo.png",
    "splashLogoUrl": "https://example.org/wp-content/uploads/splash.png",
    "primaryColor": "#1B7F5C",
    "primaryColorDark": "#3FBF8F"
  },
  "endpoints": {
    "salahApiUrl": "https://example.org/wp-json/masjid/v1/salahapi",
    "eventsUrl": "https://example.org/wp-json/masjid/v1/events",
    "announcementsUrl": "https://example.org/wp-json/masjid/v1/announcements",
    "parkingUrl": "https://example.org/wp-json/masjid/v1/parking-config"
  },
  "contact": {
    "address": "123 Example Street",
    "phone": "+1 555 0100",
    "email": "info@example.org",
    "website": "https://example.org/"
  },
  "donationUrl": "https://example.org/donate/",
  "ramadanUrl": "https://example.org/ramadan/",
  "social": {
    "facebook": "https://facebook.com/example",
    "instagram": "https://instagram.com/example",
    "whatsapp": "https://wa.me/15550100"
  },
  "featureFlags": {
    "events": true,
    "announcements": true,
    "pushNotifications": true,
    "donations": true,
    "ramadan": true,
    "parking": true,
    "qibla": true,
    "prayerReminders": true
  },
  "pushNotifications": {
    "enabled": true,
    "configured": true,
    "registrationUrl": "https://example.org/wp-json/masjid/v1/push/registrations",
    "firebase": {
      "ios": {
        "appId": "1:123456789:ios:abcdef",
        "senderId": "123456789",
        "apiKey": "public-api-key"
      },
      "android": {
        "appId": "1:123456789:android:abcdef",
        "senderId": "123456789",
        "apiKey": "public-api-key"
      }
    }
  }
}
```

All URL and contact values are strings and may be empty. `logoUrl` and
`splashLogoUrl` are empty strings when no image is configured. `ramadanUrl` is
an empty string when no Ramadan page is configured.

`endpoints.parkingUrl` is never empty. Its format is
`{siteUrl}/wp-json/masjid/v1/parking-config`: an absolute URL for
`GET /parking-config` (see [Parking Map](#parking-map)). Unlike
`donationUrl` and `ramadanUrl`, Content-only Rendering never rewrites it, and
it carries no query parameters. Clients should fetch the parking document from
this URL as returned instead of constructing it.

When Content-only Rendering is enabled under **Settings > Masjid App**, a
non-empty `donationUrl` or `ramadanUrl` automatically includes
`render=contentOnly`. Existing query parameters on either configured URL are
preserved.

`pushNotifications.configured` is true only when encrypted server credentials
and both platform configurations exist. Firebase client API keys in this
response are public application configuration; service-account credentials are
never returned.

## Push Registration

### Required App Check header

Every `POST` or `DELETE` request to `/push/registrations` **must** include a
current Firebase App Check token in this header:

```http
X-Firebase-AppCheck: <app-check-token>
```

The token must come from the MasjidApp Firebase app for the declared `platform`.
The server verifies its signature, expiration, Firebase project, and application
ID before changing any FCM topic subscription. Missing, expired, invalid, or
wrong-app tokens return `401 Unauthorized`, and no subscription change occurs.

This header is required by the API itself; it is not optional sample metadata.
Public read-only endpoints such as `/config`, `/events`, and `/announcements` do
not require App Check.

### `POST /push/registrations`

Subscribes an FCM token to this site's tenant topic. The app must initialize
Firebase with its platform configuration returned by `/config`, obtain an App
Check token from that same platform app, then register the FCM token. The `X-Firebase-AppCheck` header
described above is mandatory.

```sh
curl --fail-with-body --silent --show-error \
  --request POST \
  --header 'Content-Type: application/json' \
  --header "X-Firebase-AppCheck: $APP_CHECK_TOKEN" \
  --data '{
    "masjidId": "islamic-center-of-bothell",
    "platform": "ios",
    "token": "current-fcm-token",
    "previousToken": "previous-fcm-token"
  }' \
  "$BASE_URL/push/registrations"
```

`platform` is `ios` or `android`. `previousToken` is optional; after the new
token is subscribed successfully, it is unsubscribed to complete token
rotation. `registrationMode` is optional and selects how a site delivering
through the hosted MasjidFeed service registers the device (see below);
sites using their own Firebase project ignore it. The App Check application
ID must match the uploaded configuration for the declared platform.

Example response:

```json
{
  "registered": true
}
```

#### Hosted MasjidFeed push service

When the site delivers push through the hosted MasjidFeed service instead of
its own Firebase project, the server forwards the App Check token to the push
gateway, which verifies it against the central Firebase project. Two
registration flavors are available through the optional `registrationMode`
parameter:

- `server` (default): the FCM registration token is forwarded together with
  the App Check token, and the gateway registers the token with the site's
  canonical topic server-side (via FCM's Instance ID service). The token is
  passed through and stored by neither the site nor the gateway. `token` is
  required, as for the site-owned Firebase path.
- `client`: the gateway returns only the site's canonical topic, and the app
  subscribes to it itself (Firebase Messaging `subscribeToTopic`). `token`
  is not required for this flavor.

`client` mode answers with the `topicKey` payload:

```json
{
  "registered": false,
  "topicKey": {
    "site_id": "ms_1a2b3c4d",
    "topic": "tenant_ms_1a2b3c4d_v1_8f4e2c",
    "version": 1,
    "expires_at": "2026-11-01T12:00:00Z"
  }
}
```

In `client` mode the app subscribes to `topicKey.topic` itself; in `server`
mode the registration already happened and subscribing client-side to the
same topic is optional. Re-registering after a topic rotation moves the
device onto the new topic under both flavors.

A site configured for both delivery channels adds the site-Firebase `result`
to whichever payload the hosted service produced (`registered: true`, or
`registered: false` with `topicKey`). `DELETE /push/registrations` under the
hosted service performs no
server-side action and always answers `{"registered": false}`: unsubscription
happens client-side against the topic from `topicKey`.

### `DELETE /push/registrations`

Unsubscribes a token when the app changes tenant or disables notifications. The
`X-Firebase-AppCheck` header described above is mandatory.

```sh
curl --fail-with-body --silent --show-error \
  --request DELETE \
  --header 'Content-Type: application/json' \
  --header "X-Firebase-AppCheck: $APP_CHECK_TOKEN" \
  --data '{
    "masjidId": "islamic-center-of-bothell",
    "platform": "ios",
    "token": "current-fcm-token"
  }' \
  "$BASE_URL/push/registrations"
```

Example response:

```json
{
  "registered": false,
  "result": {
    "successful": true,
    "errorCount": 0
  }
}
```

Both operations return `401` for missing or invalid App Check tokens, `429`
after 30 attempts from one address within five minutes, and `503` when push is
disabled or incomplete. Raw FCM tokens are neither stored nor logged.

## Push Notification Payload

The server sends Firebase a display notification and a string-valued data
payload. For example, publishing a tagged announcement sends the following
message to the tenant topic:

```json
{
  "notification": {
    "title": "Parking update",
    "body": "The north parking lot will be closed this Friday. Please use the overflow parking area.",
    "image": "https://example.org/uploads/parking-update.jpg"
  },
  "data": {
    "notificationId": "b3976dc7-e67b-4cbe-9344-f79eb27078c2",
    "masjidId": "islamic-center-of-bothell",
    "postId": "456",
    "type": "announcements",
    "deepLink": "masjidapp://open/v1/masjids/islamic-center-of-bothell/announcements/456"
  },
  "apns": {
    "payload": {
      "aps": {
        "mutable-content": 1
      }
    },
    "fcm_options": {
      "image": "https://example.org/uploads/parking-update.jpg"
    }
  }
}
```

### Android (FCM)

An Android client receives the display notification and data through an FCM
`RemoteMessage`. The equivalent application-visible payload is:

```json
{
  "notification": {
    "title": "Parking update",
    "body": "The north parking lot will be closed this Friday. Please use the overflow parking area.",
    "image": "https://example.org/uploads/parking-update.jpg"
  },
  "data": {
    "notificationId": "b3976dc7-e67b-4cbe-9344-f79eb27078c2",
    "masjidId": "islamic-center-of-bothell",
    "postId": "456",
    "type": "announcements",
    "deepLink": "masjidapp://open/v1/masjids/islamic-center-of-bothell/announcements/456"
  }
}
```

### iOS (APNs via FCM)

FCM maps the same message to an APNs payload. Custom data fields are available
at the top level of the notification's `userInfo` dictionary:

```json
{
  "aps": {
    "alert": {
      "title": "Parking update",
      "body": "The north parking lot will be closed this Friday. Please use the overflow parking area."
    },
    "mutable-content": 1
  },
  "notificationId": "b3976dc7-e67b-4cbe-9344-f79eb27078c2",
  "masjidId": "islamic-center-of-bothell",
  "postId": "456",
  "type": "announcements",
  "deepLink": "masjidapp://open/v1/masjids/islamic-center-of-bothell/announcements/456",
  "fcm_options": {
    "image": "https://example.org/uploads/parking-update.jpg"
  }
}
```

FCM and APNs may add transport metadata such as message identifiers; clients
must not depend on those fields. When no valid HTTPS featured image is present,
the Android `image`, APNs `mutable-content`, and APNs `fcm_options` fields are
omitted.

`notificationId` is stable across server retries and should be used by the app
to suppress duplicate presentation. `type` is `announcements` or `events`.
`body` uses the manual post excerpt when available, otherwise a plain-text
excerpt derived from post content, truncated to 180 characters. `deepLink`
uses the configured override template when present.

When a post has a valid HTTPS featured image, Android receives it through the
common FCM `notification.image` field. iOS receives `aps.mutable-content = 1` and
`apns.fcm_options.image`; the embedded Notification Service Extension downloads the image
and attaches it. A failed, non-HTTPS, or timed-out image request degrades to the original
title and body instead of suppressing the notification.

## Prayer Times

### `GET /salahapi`

Proxies the SalahAPI 1.1 document supplied by the Muslim Prayer Times plugin.
The returned `dailyPrayerTimes.csvUrl` points to the CSV feed from that plugin.

```sh
curl --fail-with-body --silent --show-error \
  "$BASE_URL/salahapi"
```

Example response (calculation rules vary by site configuration):

```json
{
  "salahapi": "1.1",
  "info": {
    "title": "Islamic Center of Bothell Prayer Times",
    "description": "Islamic prayer times provided by Islamic Center of Bothell",
    "version": "1.0.0",
    "contact": {
      "name": "Islamic Center of Bothell",
      "email": "info@example.org"
    }
  },
  "location": {
    "latitude": 47.7623,
    "longitude": -122.2054,
    "timezone": "America/Los_Angeles",
    "dateFormat": "YYYY-MM-DD",
    "timeFormat": "h:mm A"
  },
  "calculationMethod": {
    "name": "isna",
    "asrCalculationMethod": "standard",
    "highLatitudeAdjustment": "middleOfTheNight",
    "iqamaCalculationRules": {
      "changeOn": "friday",
      "fajr": {
        "change": "weekly",
        "afterAthanMinutes": 20
      },
      "dhuhr": {
        "static": "13:30"
      },
      "asr": {
        "change": "weekly",
        "afterAthanMinutes": 15
      },
      "maghrib": {
        "change": "weekly",
        "afterAthanMinutes": 5
      },
      "isha": {
        "change": "weekly",
        "afterAthanMinutes": 15
      }
    },
    "jumuahRules": [
      {
        "name": "First Jumuah",
        "time": {
          "static": "13:30"
        }
      }
    ]
  },
  "dailyPrayerTimes": {
    "csvUrl": "https://example.org/wp-json/muslim-prayer-times/v1/prayer-times-csv?asrMethod=standard",
    "csvUrlParameters": {
      "fromDate": {
        "in": "query",
        "type": "fromDate",
        "format": "YYYY-MM-DD"
      },
      "toDate": {
        "in": "query",
        "type": "toDate",
        "format": "YYYY-MM-DD"
      }
    },
    "dateFormat": "YYYY-MM-DD",
    "timeFormat": "h:mm A"
  }
}
```

`info.contact` is omitted when the site has no valid admin email.
`calculationMethod.iqamaCalculationRules` always contains `fajr`, `dhuhr`,
`asr`, `maghrib`, and `isha`. A prayer rule is either a fixed-time object with
`static` and optional daylight-saving `overrides`, or a calculated object with
`change` (`daily` or `weekly`) and optional `roundMinutes`, `earliest`,
`latest`, `afterAthanMinutes`, `beforeEndMinutes`, and Ramadan `overrides`.
`changeOn` is present for weekly configuration. `jumuahRules` is present only
when at least one Jumuah time is configured; each item has `name` and
`time.static`.

If the Muslim Prayer Times plugin is unavailable, this endpoint returns `503`
with error code `masjidapp_missing_dependency`.

## Post Object

The `/events` and `/announcements` endpoints return arrays of the post
object returned by `/posts/{id}`. Every post contains these base fields:

| Field | Type | Notes |
| --- | --- | --- |
| `postId` | integer | WordPress post ID used by app deep links. |
| `title` | string | Rendered WordPress post title. |
| `description` | string | Full rendered HTML post content. |
| `postUrl` | string | Absolute URL for `GET /posts/{id}`. |
| `publishedAt` | string | ISO 8601 publication time in UTC. |
| `image` | string or `null` | Absolute large featured-image URL. |
| `category` | string | First non-`uncategorized` category slug, or an empty string. |
| `url` | string | Absolute WordPress permalink. |

Posts with event dates enabled also contain these event fields:

| Field | Type | Notes |
| --- | --- | --- |
| `isEvent` | boolean | Always `true`; omitted from posts without event metadata. |
| `location` | string | Empty when no location is configured. |
| `eventDateTime` | string or `null` | Next occurrence in the WordPress site timezone. |
| `recurrenceRule` | string or `null` | iCalendar recurrence rule when available. |
| `alert` | string | Optional; omitted when no alert is configured. |
| `alertEndDateTime` | string | Optional; omitted when no valid expiration is configured. |

## Events

### `GET /events`

Returns an array of published upcoming events, ordered by event date ascending.
Site settings may filter the result by category or tag.

Query parameters:

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `limit` | integer | `20` | Maximum number of events to return. Values above `100` are capped at `100`. |

```sh
curl --fail-with-body --silent --show-error \
  "$BASE_URL/events"

curl --fail-with-body --silent --show-error \
  "$BASE_URL/events?limit=50"
```

Example response:

```json
[
  {
    "postId": 123,
    "title": "Community Dinner",
    "description": "<p>Join us for dinner after Maghrib.</p>\n",
    "postUrl": "https://example.org/wp-json/masjid/v1/posts/123",
    "publishedAt": "2026-07-15T18:00:00+00:00",
    "image": "https://example.org/wp-content/uploads/community-dinner.jpg",
    "isEvent": true,
    "location": "Main prayer hall",
    "category": "community",
    "eventDateTime": "2026-08-07T19:30:00-07:00",
    "recurrenceRule": null,
    "url": "https://example.org/community-dinner/",
    "alert": "Registration required",
    "alertEndDateTime": "2026-08-07T18:00:00-07:00"
  }
]
```

If no events plugin is selected in MasjidFeed App settings, or the selected
plugin is inactive, this endpoint returns `503` with error code
`masjidapp_missing_dependency`. Supported event plugins are Awesome Calendar
Events and The Events Calendar.

## Announcements

### `GET /announcements`

Returns an array of published, non-password-protected posts, ordered by
publication date descending. Site settings may filter the result by category or
tag. Administrators may also select a Friday category that replaces the regular
announcement filters on Fridays in the WordPress site timezone.

Query parameters:

| Parameter | Type | Default | Description |
| --- | --- | --- | --- |
| `limit` | integer | `20` | Maximum number of announcements to return. Values above `100` are capped at `100`. |

```sh
curl --fail-with-body --silent --show-error \
  "$BASE_URL/announcements"

curl --fail-with-body --silent --show-error \
  "$BASE_URL/announcements?limit=50"
```

Example response:

```json
[
  {
    "postId": 456,
    "title": "Parking update",
    "description": "<p>The north parking lot is closed this Friday.</p>\n",
    "postUrl": "https://example.org/wp-json/masjid/v1/posts/456",
    "publishedAt": "2026-08-03T17:15:00+00:00",
    "image": null,
    "category": "announcements",
    "url": "https://example.org/parking-update/"
  }
]
```

## Post

### `GET /posts/{id}`

Returns one post object, including event fields when event dates are enabled.
`{id}` must be a positive integer identifying a published,
non-password-protected `post`.

```sh
POST_ID=123
curl --fail-with-body --silent --show-error \
  "$BASE_URL/posts/$POST_ID"
```

Example response:

```json
{
  "postId": 123,
  "title": "Community Dinner",
  "description": "<p>Join us for dinner after Maghrib.</p>\n",
  "postUrl": "https://example.org/wp-json/masjid/v1/posts/123",
  "publishedAt": "2026-07-15T18:00:00+00:00",
  "image": "https://example.org/wp-content/uploads/community-dinner.jpg",
  "category": "community",
  "url": "https://example.org/community-dinner/",
  "isEvent": true,
  "location": "Main prayer hall",
  "eventDateTime": "2026-08-07T19:30:00-07:00",
  "recurrenceRule": null,
  "alert": "Registration required",
  "alertEndDateTime": "2026-08-07T18:00:00-07:00"
}
```

The endpoint returns `404` with error code `masjidapp_post_not_found` when the
post is missing, is not a standard WordPress post, is not published, or is
password protected.

## Parking Map

### `GET /parking-config`

Returns the parking map document consumed by the ICOB app: the mosque's
location, the parking zone outlines, and the schedules during which
restrictions apply.

```sh
curl --fail-with-body --silent --show-error \
  --user-agent 'ICOB-iOS/2.4.1' \
  "$BASE_URL/parking-config"
```

Example response:

```json
{
  "center": { "latitude": 47.7779, "longitude": -122.1906 },
  "nearRadiusMeters": 800,
  "zones": [
    {
      "id": "north-lot",
      "name": "North lot",
      "type": "no-parking",
      "message": "Please move to a permitted parking area.",
      "coordinates": [
        { "latitude": 47.77882298016584, "longitude": -122.19132629326828 },
        { "latitude": 47.77882298016584, "longitude": -122.18952387645021 },
        { "latitude": 47.77809991713082, "longitude": -122.18952387645021 },
        { "latitude": 47.77809991713082, "longitude": -122.19132629326828 }
      ]
    },
    {
      "id": "east-street",
      "name": "East street",
      "type": "event-restricted",
      "message": "Reserved for event traffic during Friday prayer.",
      "coordinates": [
        { "latitude": 47.7782, "longitude": -122.1880 },
        { "latitude": 47.7782, "longitude": -122.1872 },
        { "latitude": 47.7772, "longitude": -122.1872 },
        { "latitude": 47.7772, "longitude": -122.1880 }
      ]
    },
    {
      "id": "overflow-lot",
      "name": "Overflow lot",
      "type": "allowed",
      "coordinates": [
        { "latitude": 47.7770, "longitude": -122.1889 },
        { "latitude": 47.7770, "longitude": -122.1875 },
        { "latitude": 47.7760, "longitude": -122.1875 },
        { "latitude": 47.7760, "longitude": -122.1889 }
      ]
    }
  ],
  "events": [
    {
      "id": "jummah",
      "name": "Friday Prayer",
      "message": "Strict parking rules are in effect for Friday prayer.",
      "schedule": { "type": "weekly", "days": [5], "start": "11:30", "end": "15:30" },
      "zoneIds": "all"
    }
  ]
}
```

Field notes:

- `center` is required; `latitude` and `longitude` are JSON numbers.
- `nearRadiusMeters` is optional and defaults to `800` when absent. How close
  to `center` counts as "near" for the parking banner.
- `zones` is required; `events` is required and may be `[]`.
- Zone `type` is the parking type for the area: one of `allowed` (green),
  `no-parking` (red), or `event-restricted` (yellow). These values are closed:
  new values need an app release.
- Zone `coordinates` are `{ "latitude", "longitude" }` objects, at least 3
  points, first point not repeated at the end; the app closes the shape
  itself. The coordinate format is closed: `[lat, lon]` arrays are not
  accepted by the app.
- `message` is optional and shown to someone standing in the zone while it is
  off limits.
- Event `schedule` is either
  `{"type": "weekly", "days": [0-6], "start": "HH:mm", "end": "HH:mm"}` or
  `{"type": "dates", "dates": ["YYYY-MM-DD"], "start": "HH:mm", "end":
  "HH:mm"}`. `days` uses 0 for Sunday through 6 for Saturday. Times are
  24-hour `HH:mm` read in the phone's local time; a window may run past
  midnight and the day/date is the one the window starts on. `start` and
  `end` always differ.
- Event `zoneIds` is `"all"` or a list of zone `id`s. While an event is
  active the app shows and checks only the listed zones. When no event is
  active every zone is shown in its own colour and `event-restricted` zones
  count as allowed.
- If two events are active at once, the app uses the one listed first.

## ETag Caching

The following responses support conditional requests:

| Endpoint | ETag | `Cache-Control` |
| --- | --- | --- |
| `GET /config` | Weak, generated from the JSON payload | `public, max-age=60, s-maxage=3600` |
| `GET /salahapi` | Strong, prayer-times update timestamp | `public, max-age=60, s-maxage=3600` |
| `GET /events` | Weak, generated from the JSON payload | `public, max-age=60, s-maxage=300` |
| `GET /announcements` | Weak, generated from the JSON payload and active filter mode | `public, max-age=60, s-maxage=600` |
| `GET /posts/{id}` | Weak, generated from the JSON payload | `public, max-age=60, s-maxage=3600` |
| `GET /parking-config` | Weak, generated from the JSON payload | `public, max-age=60, s-maxage=3600` |

Configuration, event, and post responses use a weak ETag generated from
the exact JSON payload. Announcement ETags also include whether the regular or
Friday filter is active, ensuring they change at Friday's boundaries:

```http
ETag: W/"2eb7f86a..."
```

SalahAPI uses the prayer-times last-updated value as a strong ETag. It omits the
header until prayer times have been updated at least once.

The server stores each configuration, event, announcement, and post
payload with its ETag in a WordPress transient for up to 24 hours. Relevant
content or settings changes invalidate these transients earlier. This
server-side lifetime is independent of the browser and shared-cache lifetimes
in the `Cache-Control` table above. Announcement cache lifetimes are shortened
when necessary so they do not cross into or out of Friday. SalahAPI derives its
ETag from the prayer-times update timestamp rather than this transient cache.

Store the complete header value, including the quotes and the `W/` prefix when
present, then send it in `If-None-Match` when revalidating. A matching
representation returns `304 Not Modified` with no response body. An omitted or
nonmatching validator returns `200` with the current JSON body and ETag.

Fetch a response and inspect its cache headers:

```sh
curl --silent --show-error --dump-header - \
  "$BASE_URL/events"
```

Capture the ETag and revalidate:

```sh
ETAG=$(
  curl --silent --show-error --dump-header - --output /dev/null \
    "$BASE_URL/events" \
  | awk 'tolower($1) == "etag:" { sub(/^[^:]+:[[:space:]]*/, ""); sub(/\r$/, ""); print; exit }'
)

curl --silent --show-error --dump-header - --output /dev/null \
  --header "If-None-Match: $ETAG" \
  "$BASE_URL/events"
```

Expected status line when the content has not changed:

```http
HTTP/1.1 304 Not Modified
```

The server also accepts a strong form of the current ETag, comma-separated
candidate ETags, or `If-None-Match: *`. Browsers may read `ETag` and send
`If-None-Match` in cross-origin requests because both headers are included in
the route's CORS policy.

Saving Masjid App settings invalidates the cached configuration. Saving the
parking configuration on the Parking tab invalidates the cached parking
document the same way. The next `GET /config` rebuilds the response and
derives a new ETag when its JSON payload has changed.