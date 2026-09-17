FROM golang:1.23-bookworm AS build

WORKDIR /src

COPY go.mod go.sum ./
RUN go mod download

COPY . .

# Windowsクローンからのビルド時、シバン行等は使わないためchmodは不要だが、
# 改行はgofmt/ビルドに影響しないためそのままでよい。
RUN CGO_ENABLED=0 GOOS=linux go build -o /out/relay .

FROM gcr.io/distroless/static-debian12

COPY --from=build /out/relay /relay

EXPOSE 8080

ENTRYPOINT ["/relay"]
