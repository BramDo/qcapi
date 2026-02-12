class QcapiError(Exception):
    pass


class ConfigError(QcapiError):
    pass


class HttpError(QcapiError):
    def __init__(self, status: int | None, message: str, *, url: str | None = None, body: object | None = None):
        super().__init__(message)
        self.status = status
        self.url = url
        self.body = body

