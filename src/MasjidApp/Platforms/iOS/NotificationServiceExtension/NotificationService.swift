import Foundation
import UniformTypeIdentifiers
import UserNotifications

final class NotificationService: UNNotificationServiceExtension {
    private static let maximumImageBytes: Int64 = 10 * 1024 * 1024

    private var contentHandler: ((UNNotificationContent) -> Void)?
    private var bestAttemptContent: UNMutableNotificationContent?
    private var downloadTask: URLSessionDownloadTask?
    private let lock = NSLock()

    override func didReceive(
        _ request: UNNotificationRequest,
        withContentHandler contentHandler: @escaping (UNNotificationContent) -> Void
    ) {
        guard let content = request.content.mutableCopy() as? UNMutableNotificationContent else {
            contentHandler(request.content)
            return
        }

        self.contentHandler = contentHandler
        bestAttemptContent = content

        guard let imageURL = imageURL(from: request.content.userInfo) else {
            finish(with: content)
            return
        }

        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 20
        configuration.timeoutIntervalForResource = 25
        let session = URLSession(configuration: configuration)

        downloadTask = session.downloadTask(with: imageURL) { [weak self] temporaryURL, response, _ in
            guard let self,
                  let temporaryURL,
                  let httpResponse = response as? HTTPURLResponse,
                  (200...299).contains(httpResponse.statusCode),
                  self.isSupportedImage(httpResponse, sourceURL: imageURL),
                  let attachmentURL = self.copyForAttachment(
                    temporaryURL,
                    sourceURL: imageURL,
                    mimeType: httpResponse.mimeType
                  ),
                  let attachment = try? UNNotificationAttachment(
                    identifier: "image",
                    url: attachmentURL
                  ) else {
                self?.finish(with: content)
                return
            }

            content.attachments = [attachment]
            self.finish(with: content)
        }
        downloadTask?.resume()
    }

    override func serviceExtensionTimeWillExpire() {
        downloadTask?.cancel()
        if let content = bestAttemptContent {
            finish(with: content)
        }
    }

    private func imageURL(from userInfo: [AnyHashable: Any]) -> URL? {
        let nestedImage = (userInfo["fcm_options"] as? [AnyHashable: Any])?["image"] as? String
            ?? (userInfo["fcm_options"] as? NSDictionary)?["image"] as? String
        let imageValue = nestedImage ?? userInfo["image"] as? String
        guard let imageValue,
              let url = URL(string: imageValue),
              url.scheme?.lowercased() == "https" else {
            return nil
        }

        return url
    }

    private func isSupportedImage(_ response: HTTPURLResponse, sourceURL: URL) -> Bool {
        if response.expectedContentLength > Self.maximumImageBytes {
            return false
        }

        let contentType = response.mimeType.flatMap { UTType(mimeType: $0) }
        let extensionType = UTType(filenameExtension: sourceURL.pathExtension)
        return (contentType ?? extensionType)?.conforms(to: .image) == true
    }

    private func copyForAttachment(
        _ temporaryURL: URL,
        sourceURL: URL,
        mimeType: String?
    ) -> URL? {
        let pathExtension = sourceURL.pathExtension.isEmpty
            ? mimeType.flatMap { UTType(mimeType: $0)?.preferredFilenameExtension }
            : sourceURL.pathExtension
        guard let pathExtension, !pathExtension.isEmpty else {
            return nil
        }

        let destinationURL = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString)
            .appendingPathExtension(pathExtension)
        do {
            try FileManager.default.copyItem(at: temporaryURL, to: destinationURL)
            return destinationURL
        } catch {
            return nil
        }
    }

    private func finish(with content: UNNotificationContent) {
        lock.lock()
        let handler = contentHandler
        contentHandler = nil
        lock.unlock()

        handler?(content)
    }
}