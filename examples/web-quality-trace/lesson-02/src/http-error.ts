export function httpError(response: Response, message: string) {
  const requestId = response.headers.get("X-Request-Id");
  return requestId && /^[0-9a-f]{32}$/.test(requestId)
    ? `${message} 请求编号 ${requestId}`
    : message;
}
