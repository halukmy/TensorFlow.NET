﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NumSharp;
using Tensorflow;
using Tensorflow.Keras.Engine;
using Tensorflow.Sessions;
using TensorFlowNET.Examples.Text.cnn_models;
using TensorFlowNET.Examples.TextClassification;
using TensorFlowNET.Examples.Utility;
using static Tensorflow.Python;

namespace TensorFlowNET.Examples.CnnTextClassification
{
    /// <summary>
    /// https://github.com/dongjun-Lee/text-classification-models-tf
    /// </summary>
    public class TextClassificationTrain : IExample
    {
        public int Priority => 100;
        public bool Enabled { get; set; } = false;
        public string Name => "Text Classification";
        public int? DataLimit = null;
        public bool ImportGraph { get; set; } = true;
        public bool UseSubset = true; // <----- set this true to use a limited subset of dbpedia

        private string dataDir = "text_classification";
        private string dataFileName = "dbpedia_csv.tar.gz";

        public string model_name = "vd_cnn"; // word_cnn | char_cnn | vd_cnn | word_rnn | att_rnn | rcnn

        private const string TRAIN_PATH = "text_classification/dbpedia_csv/train.csv";
        private const string SUBSET_PATH = "text_classification/dbpedia_csv/dbpedia_6400.csv";
        private const string TEST_PATH = "text_classification/dbpedia_csv/test.csv";

        private const int CHAR_MAX_LEN = 1014;
        private const int WORD_MAX_LEN = 1014;
        private const int NUM_CLASS = 14;
        private const int BATCH_SIZE = 64;
        private const int NUM_EPOCHS = 10;
        protected float loss_value = 0;

        public bool Run()
        {
            PrepareData();
            var graph = tf.Graph().as_default();
            return with(tf.Session(graph), sess =>
            {
                if (ImportGraph)
                    return RunWithImportedGraph(sess, graph);
                else
                    return RunWithBuiltGraph(sess, graph);
            });
        }

        protected virtual bool RunWithImportedGraph(Session sess, Graph graph)
        {
            var stopwatch = Stopwatch.StartNew();
            Console.WriteLine("Building dataset...");
            var path = UseSubset ? SUBSET_PATH : TRAIN_PATH;           
            var (x, y, alphabet_size) = DataHelpers.build_char_dataset(path, model_name, CHAR_MAX_LEN, DataLimit = null, shuffle:!UseSubset);
            Console.WriteLine("\tDONE ");

            var (train_x, valid_x, train_y, valid_y) = train_test_split(x, y, test_size: 0.15f);
            Console.WriteLine("Training set size: " + train_x.len);
            Console.WriteLine("Test set size: " + valid_x.len);

            Console.WriteLine("Import graph...");
            var meta_file = model_name + ".meta";
            tf.train.import_meta_graph(Path.Join("graph", meta_file));
            Console.WriteLine("\tDONE " + stopwatch.Elapsed);

            sess.run(tf.global_variables_initializer());

            var train_batches = batch_iter(train_x, train_y, BATCH_SIZE, NUM_EPOCHS);
            var num_batches_per_epoch = (len(train_x) - 1) / BATCH_SIZE + 1;
            double max_accuracy = 0;

            Tensor is_training = graph.get_tensor_by_name("is_training:0");
            Tensor model_x = graph.get_tensor_by_name("x:0");
            Tensor model_y = graph.get_tensor_by_name("y:0");
            Tensor loss = graph.get_tensor_by_name("loss/value:0");
            Tensor optimizer = graph.get_tensor_by_name("loss/optimizer:0");
            Tensor global_step = graph.get_tensor_by_name("global_step:0");
            Tensor accuracy = graph.get_tensor_by_name("accuracy/value:0");
            stopwatch = Stopwatch.StartNew();
            int i = 0;
            foreach (var (x_batch, y_batch, total) in train_batches)
            {
                i++;
                var train_feed_dict = new FeedDict
                {
                    [model_x] = x_batch,
                    [model_y] = y_batch,
                    [is_training] = true,
                };
                //Console.WriteLine("x: " + x_batch.ToString() + "\n");
                //Console.WriteLine("y: " + y_batch.ToString());
                // original python:
                //_, step, loss = sess.run([model.optimizer, model.global_step, model.loss], feed_dict = train_feed_dict)
                var result = sess.run(new ITensorOrOperation[] { optimizer, global_step, loss }, train_feed_dict);
                loss_value = result[2];
                var step = (int)result[1];
                if (step % 10 == 0 || step < 10)
                {
                    var estimate = TimeSpan.FromSeconds((stopwatch.Elapsed.TotalSeconds / i) * total);
                    Console.WriteLine($"Training on batch {i}/{total}. Estimated training time: {estimate}");
                    Console.WriteLine($"Step {step} loss: {loss_value}");
                }

                if (step % 100 == 0)
                {
                    // # Test accuracy with validation data for each epoch.
                    var valid_batches = batch_iter(valid_x, valid_y, BATCH_SIZE, 1);
                    var (sum_accuracy, cnt) = (0.0f, 0);
                    foreach (var (valid_x_batch, valid_y_batch, total_validation_batches) in valid_batches)
                    {
                        var valid_feed_dict = new FeedDict
                        {
                            [model_x] = valid_x_batch,
                            [model_y] = valid_y_batch,
                            [is_training] = false
                        };
                        var result1 = sess.run(accuracy, valid_feed_dict);
                        float accuracy_value = result1;
                        sum_accuracy += accuracy_value;
                        cnt += 1;
                    }

                    var valid_accuracy = sum_accuracy / cnt;

                    print($"\nValidation Accuracy = {valid_accuracy}\n");

                    //    # Save model
                    //        if valid_accuracy > max_accuracy:
                    //        max_accuracy = valid_accuracy
                    //        saver.save(sess, "{0}/{1}.ckpt".format(args.model, args.model), global_step = step)
                    //        print("Model is saved.\n")
                }
            }

            return false;
        }

        protected virtual bool RunWithBuiltGraph(Session session, Graph graph)
        {
            Console.WriteLine("Building dataset...");
            var (x, y, alphabet_size) = DataHelpers.build_char_dataset("train", model_name, CHAR_MAX_LEN, DataLimit);

            var (train_x, valid_x, train_y, valid_y) = train_test_split(x, y, test_size: 0.15f);

            ITextClassificationModel model = null;
            switch (model_name) // word_cnn | char_cnn | vd_cnn | word_rnn | att_rnn | rcnn
            {
                case "word_cnn":
                case "char_cnn":
                case "word_rnn":
                case "att_rnn":
                case "rcnn":
                    throw new NotImplementedException();
                    break;
                case "vd_cnn":
                    model = new VdCnn(alphabet_size, CHAR_MAX_LEN, NUM_CLASS);
                    break;
            }
            // todo train the model
            return false;
        }

        // TODO: this originally is an SKLearn utility function. it randomizes train and test which we don't do here
        private (NDArray, NDArray, NDArray, NDArray) train_test_split(NDArray x, NDArray y, float test_size = 0.3f)
        {
            Console.WriteLine("Splitting in Training and Testing data...");
            int len = x.shape[0];
            //int classes = y.Data<int>().Distinct().Count();
            //int samples = len / classes;
            int train_size = (int)Math.Round(len * (1 - test_size));
            var train_x = x[new Slice(stop: train_size), new Slice()];
            var valid_x = x[new Slice(start: train_size + 1), new Slice()];
            var train_y = y[new Slice(stop: train_size)];
            var valid_y = y[new Slice(start: train_size + 1)];
            Console.WriteLine("\tDONE");
            return (train_x, valid_x, train_y, valid_y);
        }

        //private (int[][], int[][], int[], int[]) train_test_split(int[][] x, int[] y, float test_size = 0.3f)
        //{
        //    Console.WriteLine("Splitting in Training and Testing data...");
        //    var stopwatch = Stopwatch.StartNew();
        //    int len = x.Length;
        //    int train_size = int.Parse((len * (1 - test_size)).ToString());
        //    var random = new Random(17);

        //    // we collect indices of labels 
        //    var labels = new Dictionary<int, HashSet<int>>();
        //    var shuffled_indices = random.Shuffle<int>(range(len).ToArray());
        //    foreach (var i in shuffled_indices)
        //    {
        //        var label = y[i];
        //        if (!labels.ContainsKey(i))
        //            labels[label] = new HashSet<int>();
        //        labels[label].Add(i);
        //    }

        //    var train_x = new int[train_size][];
        //    var valid_x = new int[len - train_size][];
        //    var train_y = new int[train_size];
        //    var valid_y = new int[len - train_size];

        //    FillWithShuffledLabels(x, y, train_x, train_y, random, labels);
        //    FillWithShuffledLabels(x, y, valid_x, valid_y, random, labels);

        //    Console.WriteLine("\tDONE " + stopwatch.Elapsed);
        //    return (train_x, valid_x, train_y, valid_y);
        //}

        private static void FillWithShuffledLabels(int[][] x, int[] y, int[][] shuffled_x, int[] shuffled_y, Random random, Dictionary<int, HashSet<int>> labels)
        {
            int i = 0;
            var label_keys = labels.Keys.ToArray();
            while (i < shuffled_x.Length)
            {
                var key = label_keys[random.Next(label_keys.Length)];
                var set = labels[key];
                var index = set.First();
                if (set.Count == 0)
                {
                    labels.Remove(key); // remove the set as it is empty
                    label_keys = labels.Keys.ToArray();
                }
                shuffled_x[i] = x[index];
                shuffled_y[i] = y[index];
                i++;
            }
        }

        private IEnumerable<(NDArray, NDArray, int)> batch_iter(NDArray inputs, NDArray outputs, int batch_size, int num_epochs)
        {
            var num_batches_per_epoch = (len(inputs) - 1) / batch_size + 1;
            var total_batches = num_batches_per_epoch * num_epochs;
            foreach (var epoch in range(num_epochs))
            {
                foreach (var batch_num in range(num_batches_per_epoch))
                {
                    var start_index = batch_num * batch_size;
                    var end_index = Math.Min((batch_num + 1) * batch_size, len(inputs));
                    if (end_index <= start_index)
                        break;
                    yield return (inputs[new Slice(start_index, end_index)], outputs[new Slice(start_index, end_index)], total_batches);
                }
            }
        }

        public void PrepareData()
        {
            if (UseSubset)
            {
                var url = "https://raw.githubusercontent.com/SciSharp/TensorFlow.NET/master/data/dbpedia_subset.zip";
                Web.Download(url, dataDir, "dbpedia_subset.zip");
                Compress.UnZip(Path.Combine(dataDir, "dbpedia_subset.zip"), Path.Combine(dataDir, "dbpedia_csv"));
            }
            else
            {
                string url = "https://github.com/le-scientifique/torchDatasets/raw/master/dbpedia_csv.tar.gz";
                Web.Download(url, dataDir, dataFileName);
                Compress.ExtractTGZ(Path.Join(dataDir, dataFileName), dataDir);
            }

            if (ImportGraph)
            {
                // download graph meta data
                var meta_file = model_name + ".meta";
                var meta_path = Path.Combine("graph", meta_file);
                if (File.GetLastWriteTime(meta_path) < new DateTime(2019, 05, 11))
                {
                    // delete old cached file which contains errors
                    Console.WriteLine("Discarding cached file: " + meta_path);
                    File.Delete(meta_path);
                }
                var url = "https://raw.githubusercontent.com/SciSharp/TensorFlow.NET/master/graph/" + meta_file;
                Web.Download(url, "graph", meta_file);
            }
        }
    }
}
