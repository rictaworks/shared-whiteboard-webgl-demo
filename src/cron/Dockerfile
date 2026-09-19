# Issue #11：日次リセットを backend へ HTTP で依頼するだけの Cron 専用イメージ。
# Railway の startCommand 経由で curl の引数やシェル構文を渡すと解釈が不安定だったため、
# スクリプトをイメージに焼き込む。親イメージの ENTRYPOINT(/entrypoint.sh) は明示的に外し、
# Railway 側の startCommand を /daily_reset.sh にしても、未設定(CMD)でも同じ結果になるようにする。
FROM curlimages/curl:8.10.1

COPY --chmod=755 daily_reset.sh /daily_reset.sh

ENTRYPOINT []
CMD ["/daily_reset.sh"]
