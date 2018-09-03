# CCL-GPU

Connected component labeling (CCL) using Unity ComputeShader

![screenshot](ss.png)

CUDAの実装とか、読みながら作ってみたけど、SharedMemoryを使って最適化するとことか良く分らなかった

Labeling自体はそれなりに早く処理できるけど、BlobDataを作るのにデータをCPU側に渡したり、かたまり毎のポイントの処理をCPUで行っているので、ボトルネックになっている感じ