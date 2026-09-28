<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Support\Str;

class PaymentRefund extends Model
{
    use HasFactory;

    protected $table = 'payment_refunds';

    protected $keyType = 'string';
    public $incrementing = false;

    protected $fillable = [
        'id',
        'booking_id',
        'user_id',
        'user_name',
        'amount',
        'reason',
        'note',
        'status',
    ];

    protected static function booted(): void
    {
        static::creating(function (PaymentRefund $ref) {
            if (empty($ref->id)) {
                $ref->id = 'ref_' . Str::lower(Str::random(12));
            }
            if (empty($ref->status)) {
                $ref->status = 'Pending';
            }
        });
    }
}
