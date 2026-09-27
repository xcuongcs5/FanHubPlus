<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Str;

class PaymentTransaction extends Model
{
    use HasFactory;

    protected $table = 'payment_transactions';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'user_id',
        'user_name',
        'amount',
        'provider',
        'merchant_ref',
        'status',
    ];

    protected static function booted(): void
    {
        static::creating(function (PaymentTransaction $tx) {
            if (empty($tx->id)) {
                $tx->id = 'tx_' . Str::lower(Str::random(12));
            }
            if (empty($tx->status)) {
                $tx->status = 'Success';
            }
        });
    }
}
